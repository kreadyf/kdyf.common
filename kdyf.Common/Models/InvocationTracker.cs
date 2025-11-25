using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kdyf.Common.Models
{
    /// <summary>
    /// Thread-safe tracker for recording method invocations.
    /// Used to prevent duplicate service registrations or repeated operations.
    /// </summary>
    public class InvocationTracker
    {
        /// <summary>
        /// Gets the concurrent dictionary of invoked methods, keyed by identifier.
        /// </summary>
        public ConcurrentDictionary<string, string> InvokedMethods { get; } = new ConcurrentDictionary<string, string>();
    }
}
