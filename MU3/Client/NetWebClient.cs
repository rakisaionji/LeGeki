using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Cache;
using System.Threading;

namespace MU3.Client
{
	public class NetWebClient
	{
		public int TimeOutInMSec
		{
			get
			{
				return this.timeoutMSec_;
			}
			set
			{
				this.timeoutMSec_ = value;
			}
		}

		public WebExceptionStatus WebException
		{
			get
			{
				return this.webExceptionStatus_;
			}
		}

		public string Error
		{
			get
			{
				return this.error_;
			}
		}

		public int State
		{
			get
			{
				int result;
				lock (this)
				{
					result = this.state_;
				}
				return result;
			}
			set
			{
				lock (this)
				{
					this.state_ = value;
				}
			}
		}

		public int HttpStatus
		{
			get
			{
				return this.httpStatusCode_;
			}
		}

		public MemoryStream getResponse()
		{
			return this.memoryStream_;
		}

		public static NetWebClient Create(string url)
		{
			NetWebClient netWebClient = new NetWebClient();
			NetWebClient result;
			try
			{
				netWebClient.request_ = (WebRequest.Create(url) as HttpWebRequest);
				netWebClient.request_.CachePolicy = netWebClient.cachePolicy_;
				netWebClient.State = 1;
				result = netWebClient;
			}
			catch (Exception)
			{
				result = null;
			}
			return result;
		}

		public void dispose()
		{
			this.releaseTimeoutWaitHandle();
			this.destroyRequest();
			this.destroyReponse();
			this.memoryStream_.Close();
		}

		public bool create(string uri)
		{
			this.releaseTimeoutWaitHandle();
			this.destroyRequest();
			this.destroyReponse();
			this.resetStatus();
			this.State = 0;
			bool result;
			try
			{
				this.request_ = (WebRequest.Create(uri) as HttpWebRequest);
				this.request_.CachePolicy = this.cachePolicy_;
				this.State = 1;
				result = true;
			}
			catch (Exception ex)
			{
				this.error_ = ex.ToString();
				result = false;
			}
			return result;
		}

		public bool request(byte[] bytes, string userAgent, bool compress, string method = "POST")
		{
			this.request_.Method = method;
			this.request_.ContentType = "application/json";
			this.request_.UserAgent = ((!string.IsNullOrEmpty(userAgent)) ? userAgent : string.Empty);
			this.request_.Headers.Add("charset", "UTF-8");
			this.encoding_ = ((!compress) ? NetWebClient.Encoding.Raw : NetWebClient.Encoding.Deflate);
			this.resetStatus();
			bool result;
			try
			{
				if (bytes != null && 0 < bytes.Length)
				{
					if (compress)
					{
						this.request_.Headers.Add(HttpRequestHeader.ContentEncoding, "deflate");
					}
					this.writeToRequest(bytes);
					this.State = 2;
					result = true;
				}
				else
				{
					this.request_.ContentLength = 0L;
					result = this.request();
				}
			}
			catch (WebException ex)
			{
				this.setError(ex.Status, ex.Message, 5, ex.Response as HttpWebResponse);
				result = false;
			}
			catch (Exception ex2)
			{
				this.setError(WebExceptionStatus.UnknownError, ex2.Message, 5, null);
				result = false;
			}
			return result;
		}

		public bool request()
		{
			bool result;
			try
			{
				this.releaseTimeoutWaitHandle();
				IAsyncResult asyncResult = this.request_.BeginGetResponse(new AsyncCallback(NetWebClient.responseCallback), this);
				this.waitHandle_ = asyncResult.AsyncWaitHandle;
				this.timeoutWaitHandle_ = ThreadPool.RegisterWaitForSingleObject(this.waitHandle_, new WaitOrTimerCallback(NetWebClient.timeoutCallback), this, this.timeoutMSec_, true);
				this.State = 3;
				result = true;
			}
			catch (WebException ex)
			{
				this.setError(ex.Status, ex.Message, 5, ex.Response as HttpWebResponse);
				result = false;
			}
			catch (Exception ex2)
			{
				this.setError(WebExceptionStatus.UnknownError, ex2.Message, 5, null);
				result = false;
			}
			return result;
		}

		private void destroyRequest()
		{
			if (this.request_ != null)
			{
				this.request_.Abort();
				this.request_ = null;
			}
			this.bytes_ = null;
		}

		private void destroyReponse()
		{
			if (this.compressedStream_ != null)
			{
				this.compressedStream_.Close();
				this.compressedStream_ = null;
			}
			if (this.responseStream_ != null)
			{
				this.responseStream_.Close();
				this.responseStream_ = null;
			}
			if (this.response_ != null)
			{
				this.httpStatusCode_ = (int)this.response_.StatusCode;
				this.response_.Close();
				this.response_ = null;
			}
		}

		private void resetStatus()
		{
			this.webExceptionStatus_ = WebExceptionStatus.Success;
			this.httpStatusCode_ = -1;
			this.error_ = string.Empty;
		}

		private void releaseTimeoutWaitHandle()
		{
			if (this.timeoutWaitHandle_ != null)
			{
				if (this.waitHandle_ != null)
				{
					this.timeoutWaitHandle_.Unregister(this.waitHandle_);
				}
				this.timeoutWaitHandle_ = null;
			}
			if (this.waitHandle_ != null)
			{
				this.waitHandle_.Close();
				this.waitHandle_ = null;
			}
		}

		private static void timeoutCallback(object state, bool timedout)
		{
			if (!timedout)
			{
				return;
			}
			NetWebClient netWebClient = state as NetWebClient;
			if (netWebClient.request_ == null)
			{
				netWebClient.setError(WebExceptionStatus.UnknownError, string.Empty, 5, null);
				return;
			}
			netWebClient.setError(WebExceptionStatus.Timeout, string.Empty, 5, null);
		}

		private void setError(WebExceptionStatus status, string error, int state, HttpWebResponse response)
		{
			this.webExceptionStatus_ = status;
			this.error_ = error;
			this.releaseTimeoutWaitHandle();
			this.destroyReponse();
			if (response != null)
			{
				this.httpStatusCode_ = (int)response.StatusCode;
			}
			this.State = state;
		}

		private void setSuccess(int state)
		{
			this.webExceptionStatus_ = WebExceptionStatus.Success;
			this.error_ = string.Empty;
			this.destroyReponse();
			this.State = state;
		}

		private static void requestCallback(IAsyncResult asynchronousResult)
		{
			NetWebClient netWebClient = asynchronousResult.AsyncState as NetWebClient;
			Stream stream = null;
			try
			{
				HttpWebRequest httpWebRequest = netWebClient.request_;
				stream = httpWebRequest.EndGetRequestStream(asynchronousResult);
				NetWebClient.Encoding encoding = netWebClient.encoding_;
				if (encoding != NetWebClient.Encoding.Deflate)
				{
					if (encoding != NetWebClient.Encoding.GZip)
					{
						stream.Write(netWebClient.bytes_, 0, netWebClient.bytes_.Length);
					}
					else
					{
						using (GZipStream gzipStream = new GZipStream(stream, CompressionMode.Compress, true))
						{
							gzipStream.Write(netWebClient.bytes_, 0, netWebClient.bytes_.Length);
						}
					}
				}
				else
				{
					uint num = NetWebClient.calcAdler32(netWebClient.bytes_.LongLength, netWebClient.bytes_);
					stream.WriteByte(120);
					stream.WriteByte(156);
					if (netWebClient.bytes_.Length <= 128)
					{
						NetWebClient.deflateRawBlock(stream, netWebClient.bytes_, 0, netWebClient.bytes_.Length);
					}
					else
					{
						using (DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Compress, true))
						{
							deflateStream.Write(netWebClient.bytes_, 0, netWebClient.bytes_.Length);
						}
					}
					stream.WriteByte((byte)(num >> 24 & 255U));
					stream.WriteByte((byte)(num >> 16 & 255U));
					stream.WriteByte((byte)(num >> 8 & 255U));
					stream.WriteByte((byte)(num >> 0 & 255U));
				}
				netWebClient.request();
			}
			catch (WebException ex)
			{
				netWebClient.setError(ex.Status, ex.Message, 5, ex.Response as HttpWebResponse);
			}
			catch (Exception ex2)
			{
				netWebClient.setError(WebExceptionStatus.UnknownError, ex2.Message, 5, null);
			}
			finally
			{
				netWebClient.bytes_ = null;
				if (stream != null)
				{
					stream.Close();
				}
			}
		}

		private static void responseCallback(IAsyncResult asynchronousResult)
		{
			NetWebClient netWebClient = asynchronousResult.AsyncState as NetWebClient;
			try
			{
				HttpWebRequest httpWebRequest = netWebClient.request_;
				netWebClient.response_ = (httpWebRequest.EndGetResponse(asynchronousResult) as HttpWebResponse);
				netWebClient.responseStream_ = netWebClient.response_.GetResponseStream();
				netWebClient.memoryStream_.SetLength(0L);
				netWebClient.memoryStream_.Seek(0L, SeekOrigin.Begin);
				NetWebClient.Encoding encoding = netWebClient.encoding_;
				if (encoding == NetWebClient.Encoding.Deflate || encoding == NetWebClient.Encoding.GZip)
				{
					netWebClient.compressedStream_ = new MemoryStream((0L >= netWebClient.response_.ContentLength) ? 1024 : ((int)netWebClient.response_.ContentLength));
				}
				netWebClient.responseStream_.BeginRead(netWebClient.buffer_, 0, 1024, new AsyncCallback(NetWebClient.readCallback), netWebClient);
			}
			catch (WebException ex)
			{
				netWebClient.setError(ex.Status, ex.Message, 5, ex.Response as HttpWebResponse);
			}
			catch (Exception ex2)
			{
				netWebClient.setError(WebExceptionStatus.UnknownError, ex2.Message, 5, null);
			}
		}

		private static void copyTo(Stream outStream, Stream inStream, byte[] buffer, int bufferSize)
		{
			for (;;)
			{
				int num = inStream.Read(buffer, 0, bufferSize);
				if (num <= 0)
				{
					break;
				}
				outStream.Write(buffer, 0, num);
			}
		}

		private static void readCallback(IAsyncResult asynchronousResult)
		{
			NetWebClient netWebClient = asynchronousResult.AsyncState as NetWebClient;
			try
			{
				Stream stream = netWebClient.responseStream_;
				int num = stream.EndRead(asynchronousResult);
				if (0 < num)
				{
					if (netWebClient.compressedStream_ != null)
					{
						netWebClient.compressedStream_.Write(netWebClient.buffer_, 0, num);
					}
					else
					{
						netWebClient.memoryStream_.Write(netWebClient.buffer_, 0, num);
					}
					stream.BeginRead(netWebClient.buffer_, 0, 1024, new AsyncCallback(NetWebClient.readCallback), netWebClient);
				}
				else if (asynchronousResult.IsCompleted)
				{
					NetWebClient.Encoding encoding = netWebClient.encoding_;
					if (encoding != NetWebClient.Encoding.Deflate)
					{
						if (encoding == NetWebClient.Encoding.GZip)
						{
							netWebClient.compressedStream_.Seek(0L, SeekOrigin.Begin);
							using (GZipStream gzipStream = new GZipStream(netWebClient.compressedStream_, CompressionMode.Decompress, true))
							{
								NetWebClient.copyTo(netWebClient.memoryStream_, gzipStream, netWebClient.buffer_, 1024);
							}
						}
					}
					else
					{
						netWebClient.compressedStream_.Seek(0L, SeekOrigin.Begin);
						netWebClient.compressedStream_.ReadByte();
						netWebClient.compressedStream_.ReadByte();
						using (DeflateStream deflateStream = new DeflateStream(netWebClient.compressedStream_, CompressionMode.Decompress, true))
						{
							NetWebClient.copyTo(netWebClient.memoryStream_, deflateStream, netWebClient.buffer_, 1024);
						}
						if (!NetWebClient.checkHash(netWebClient.compressedStream_, netWebClient.memoryStream_))
						{
							netWebClient.setError(WebExceptionStatus.UnknownError, "Invalid Hash", 5, null);
							return;
						}
					}
					netWebClient.setSuccess(4);
				}
			}
			catch (WebException ex)
			{
				netWebClient.setError(ex.Status, ex.Message, 5, ex.Response as HttpWebResponse);
			}
			catch (Exception ex2)
			{
				netWebClient.setError(WebExceptionStatus.UnknownError, ex2.Message, 5, null);
			}
		}

		private void writeToRequest(byte[] bytes)
		{
			try
			{
				this.bytes_ = bytes;
				this.request_.BeginGetRequestStream(new AsyncCallback(NetWebClient.requestCallback), this);
			}
			catch
			{
				this.bytes_ = null;
				throw;
			}
		}

		private static void write(Stream stream, ushort x)
		{
			stream.WriteByte((byte)(x & 255));
			stream.WriteByte((byte)(x >> 8 & 255));
		}

		private static void deflateRawBlock(Stream stream, byte[] buffer, int offset, int size)
		{
			byte value = 1;
			ushort num = (ushort)size;
			ushort x = unchecked((ushort)~num);
			stream.WriteByte(value);
			NetWebClient.write(stream, num);
			NetWebClient.write(stream, x);
			stream.Write(buffer, offset, size);
		}

		private static uint calcAdler32(long length, byte[] buffer)
		{
			uint num = 1U;
			uint num2 = 0U;
			for (long num3 = 0L; num3 < length; num3 += 1L)
			{
				uint num4 = (uint)buffer[(int)(checked((IntPtr)num3))];
				num = (num + num4) % 65521U;
				num2 = (num2 + num) % 65521U;
			}
			return (num2 << 16) + num;
		}

		private static uint calcAdler32(MemoryStream stream)
		{
			stream.Seek(0L, SeekOrigin.Begin);
			uint num = 1U;
			uint num2 = 0U;
			for (long num3 = 0L; num3 < stream.Length; num3 += 1L)
			{
				uint num4 = (uint)stream.ReadByte();
				num = (num + num4) % 65521U;
				num2 = (num2 + num) % 65521U;
			}
			stream.Seek(0L, SeekOrigin.Begin);
			return (num2 << 16) + num;
		}

		private static bool checkHash(Stream received, MemoryStream decompressed)
		{
			if (received.Length < 4L)
			{
				return false;
			}
			received.Seek(-4L, SeekOrigin.End);
			uint num = (uint)((uint)((byte)received.ReadByte()) << 24);
			num |= (uint)((uint)((byte)received.ReadByte()) << 16);
			num |= (uint)((uint)((byte)received.ReadByte()) << 8);
			num |= (uint)((uint)((byte)received.ReadByte()) << 0);
			uint num2 = NetWebClient.calcAdler32(decompressed);
			return num2 == num;
		}

		public const int DefaultTimeout = 60000;

		public const int BufferSize = 1024;

		public const string GET = "GET";

		public const string POST = "POST";

		public const string ContentType_Json = "application/json";

		public const string ContentEncoding_Deflate = "deflate";

		public const int MaxRawByteSize = 128;

		public const byte DEFLATE_BLOCK_END_FLAG = 1;

		public const byte DEFLATE_BLOCK_TYPE_NOCOMPRESSION = 0;

		public const int State_Init = 0;

		public const int State_Ready = 1;

		public const int State_Request = 2;

		public const int State_Process = 3;

		public const int State_Done = 4;

		public const int State_Error = 5;

		private HttpWebRequest request_;

		private HttpWebResponse response_;

		private WaitHandle waitHandle_;

		private RegisteredWaitHandle timeoutWaitHandle_;

		private int timeoutMSec_ = 60000;

		private RequestCachePolicy cachePolicy_ = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

		private Stream responseStream_;

		private byte[] buffer_ = new byte[1024];

		private MemoryStream memoryStream_ = new MemoryStream(1024);

		private Stream compressedStream_;

		private int state_;

		private WebExceptionStatus webExceptionStatus_;

		private int httpStatusCode_ = -1;

		private string error_ = string.Empty;

		private byte[] bytes_;

		private NetWebClient.Encoding encoding_;

		public enum Encoding
		{
			Raw,
			Deflate,
			GZip
		}
	}
}
