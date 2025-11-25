using kdyf.Notifications.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Notifications.Redis.Integration
{
    /// <summary>
    /// Marker interface for Redis-based notification emitter.
    /// Extends <see cref="INotificationEmitter"/> for dependency injection differentiation.
    /// </summary>
    public interface IRedisNotificationEmitter : INotificationEmitter
    {
    }
}
