using Gallop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UmamusumeResponseAnalyzer.Communications
{
    public class BaseSubscription<T>(string webSocketKey) : ICommand
    {
        public CommandType CommandType { get => CommandType.Subscribe; }
        public string WebSocketKey { get; init; } = webSocketKey;

        private async void Handler(object? _, T e)
        {
            if (!await Server.Send(WebSocketKey, e!))
                BaseSubscriptionHandler -= Handler;
        }
        public WSResponse? Execute()
        {
            BaseSubscriptionHandler += Handler;
            SubscribedClients.Add(WebSocketKey, this);
            return new WSResponse() { Result = WSResponse.WSResponseResultCode.Success };
        }
        public WSResponse? Cancel()
        {
            var response = new WSResponse() { Result = WSResponse.WSResponseResultCode.Success };
            if (SubscribedClients.TryGetValue(WebSocketKey, out BaseSubscription<T>? value))
            {
                BaseSubscriptionHandler -= value.Handler;
                SubscribedClients.Remove(WebSocketKey);
            }
            else
            {
                response.Result = WSResponse.WSResponseResultCode.Fail;
                response.Reason = $"未找到SecKey为{WebSocketKey}的订阅";
            }
            return response;
        }

        public static readonly Dictionary<string, BaseSubscription<T>> SubscribedClients = [];
        public static event EventHandler<T> BaseSubscriptionHandler;
        public static int Signal(T ev)
        {
            if (BaseSubscriptionHandler != null)
            {
                foreach (var del in BaseSubscriptionHandler.GetInvocationList().Cast<EventHandler<T>>())
                {
                    del.Invoke(null, ev);
                }
                return BaseSubscriptionHandler.GetInvocationList().Length;
            }
            else
                return 0;
        }
    }
}
