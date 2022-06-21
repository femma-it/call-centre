using System;
using Microsoft.AspNetCore.SignalR.Client;

namespace CallCentre
{
    public class RetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            return TimeSpan.FromSeconds(5);
        }
    }
}