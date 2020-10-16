using System;
using System.IO;
using System.Net;
using System.Text;
using MU3.DB;
using MU3.Operation;
using MU3.Util;
using UnityEngine;

namespace MU3.Client
{
	public abstract class Packet
	{
		public Packet.State state
		{
			get
			{
				return this.state_;
			}
		}

		public Packet.Status status
		{
			get
			{
				return this.status_;
			}
		}

		public int httpStatus
		{
			get
			{
				return this.httpStatus_;
			}
		}

		public WebExceptionStatus webException
		{
			get
			{
				return this.webExceptionStatus_;
			}
		}

		public string errorString
		{
			get
			{
				return this.error_;
			}
		}

		public INetQuery query
		{
			get
			{
				return this.query_;
			}
		}

		public static string toString(Packet.Status status)
		{
			switch (status + 7)
			{
			case Packet.Status.OK:
				return "HTTPエラー";
			case (Packet.Status)1:
				return "内部システムエラー";
			case (Packet.Status)2:
				return "接続エラー";
			case (Packet.Status)3:
				return "タイムアウト";
			case (Packet.Status)4:
				return "応答メッセージデコードエラー";
			case (Packet.Status)5:
				return "要求メッセージエンコードエラー";
			case (Packet.Status)6:
				return "ソケット生成エラー";
			case (Packet.Status)7:
				return "成功";
			default:
				return string.Empty;
			}
		}

		private static void onFinishErrorDialog(int selected, bool flag)
		{
			SingletonMonoBehaviour<RootScript>.instance.forceToAdvertise();
		}

		public static void createErrorDialog(Packet packet, UIDialog.OnFinish onFinishErrorDialog)
		{
			UIDialog.create(DialogID.NetworkError, onFinishErrorDialog);
		}

		public abstract Packet.State proc();

		protected bool create(INetQuery query)
		{
			OperationManager instance = Singleton<OperationManager>.instance;
			string baseUri = instance.getBaseUri();
			this.query_ = query;
			this.client_ = NetWebClient.Create(baseUri + query.URL);
			this.retryCount_ = 0;
			this.time_ = 0f;
			if (this.client_ != null)
			{
				this.client_.TimeOutInMSec = NetConfig.TimeOutInMSec;
				this.state_ = Packet.State.Ready;
				this.status_ = Packet.Status.OK;
				return true;
			}
			this.state_ = Packet.State.Error;
			this.status_ = Packet.Status.Error_Create;
			return false;
		}

		protected bool reset()
		{
			OperationManager instance = Singleton<OperationManager>.instance;
			string baseUri = instance.getBaseUri();
			this.client_ = NetWebClient.Create(baseUri + this.query.URL);
			this.time_ = 0f;
			if (this.client_ != null)
			{
				this.client_.TimeOutInMSec = NetConfig.TimeOutInMSec;
				this.state_ = Packet.State.Ready;
				this.status_ = Packet.Status.OK;
				return true;
			}
			this.state_ = Packet.State.Error;
			this.status_ = Packet.Status.Error_Create;
			return false;
		}

		protected Packet.State procImpl()
		{
			Packet.State state = this.state_;
			Packet.State state2 = this.state_;
			if (state2 != Packet.State.Ready)
			{
				if (state2 != Packet.State.Process)
				{
					if (state2 == Packet.State.RetryWait)
					{
						this.time_ += Time.deltaTime;
						if (NetConfig.RetryWaitInSec <= this.time_)
						{
							this.reset();
						}
					}
				}
				else
				{
					int state3 = this.client_.State;
					if (state3 != 4)
					{
						if (state3 == 5)
						{
							this.procResult();
						}
					}
					else
					{
						MemoryStream response = this.client_.getResponse();
						byte[] buffer = response.GetBuffer();
						string response2 = (buffer == null) ? string.Empty : Encoding.UTF8.GetString(buffer, 0, (int)response.Length);
						try
						{
							if (this.query_.setResponse(response2))
							{
								this.state_ = Packet.State.Done;
								this.status_ = Packet.Status.OK;
							}
							else
							{
								this.state_ = Packet.State.Error;
								this.status_ = Packet.Status.Error_DecodeResponse;
							}
						}
						catch
						{
							this.state_ = Packet.State.Error;
							this.status_ = Packet.Status.Error_DecodeResponse;
						}
						this.clear();
					}
				}
			}
			else
			{
				try
				{
					string request = this.query_.getRequest();
					if (!this.client_.request(Encoding.UTF8.GetBytes(request), NetPacketUtil.getUserAgent(this.query_), this.query_.Compress, "POST"))
					{
						this.procResult();
					}
					else
					{
						this.state_ = Packet.State.Process;
					}
				}
				catch
				{
					this.state_ = Packet.State.Error;
					this.status_ = Packet.Status.Error_EcodeRequest;
				}
			}
			if (state != this.state_ && (this.state_ == Packet.State.Done || this.state_ == Packet.State.Error))
			{
				Singleton<OperationManager>.instance.onPacketFinish(this.status_);
			}
			return this.state_;
		}

		protected void clear()
		{
			if (this.client_ != null)
			{
				this.httpStatus_ = this.client_.HttpStatus;
				this.webExceptionStatus_ = this.client_.WebException;
				this.error_ = this.client_.Error;
				this.client_.dispose();
				this.client_ = null;
			}
		}

		protected void procResult()
		{
			this.clear();
			switch (this.webExceptionStatus_)
			{
			case WebExceptionStatus.Success:
				if (this.httpStatus_ == 200)
				{
					this.state_ = Packet.State.Done;
					this.status_ = Packet.Status.OK;
				}
				else
				{
					this.state_ = Packet.State.Error;
					this.status_ = Packet.Status.Error_HttpStatus;
				}
				return;
			case WebExceptionStatus.NameResolutionFailure:
			case WebExceptionStatus.ConnectFailure:
			case WebExceptionStatus.ReceiveFailure:
			case WebExceptionStatus.SendFailure:
				this.status_ = Packet.Status.Error_Connection;
				goto IL_D3;
			case WebExceptionStatus.PipelineFailure:
			case WebExceptionStatus.RequestCanceled:
			case WebExceptionStatus.ProtocolError:
			case WebExceptionStatus.ConnectionClosed:
			case WebExceptionStatus.TrustFailure:
			case WebExceptionStatus.SecureChannelFailure:
			case WebExceptionStatus.ServerProtocolViolation:
			case WebExceptionStatus.KeepAliveFailure:
			case WebExceptionStatus.Pending:
				this.status_ = Packet.Status.Error_Internal;
				goto IL_D3;
			case WebExceptionStatus.Timeout:
				this.status_ = Packet.Status.Error_Timeout;
				goto IL_D3;
			}
			this.status_ = Packet.Status.Error_Internal;
			IL_D3:
			if (this.retryCount_ < NetConfig.MaxRetry)
			{
				this.retryCount_++;
				this.state_ = Packet.State.RetryWait;
			}
			else
			{
				this.state_ = Packet.State.Error;
			}
		}

		protected Packet.State state_;

		protected Packet.Status status_;

		protected int httpStatus_ = -1;

		protected WebExceptionStatus webExceptionStatus_;

		protected string error_ = string.Empty;

		protected INetQuery query_;

		protected NetWebClient client_;

		protected int retryCount_;

		protected float time_;

		public enum State
		{
			Ready,
			Process,
			Done,
			RetryWait,
			Dialog,
			Error
		}

		public enum Status
		{
			OK,
			Error_Create = -1,
			Error_EcodeRequest = -2,
			Error_DecodeResponse = -3,
			Error_Timeout = -4,
			Error_Connection = -5,
			Error_Internal = -6,
			Error_HttpStatus = -7
		}
	}
}
