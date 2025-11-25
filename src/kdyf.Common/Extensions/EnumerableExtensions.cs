using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Linq
{
    /// <summary>
    /// Extension methods for <see cref="IEnumerable{T}"/> collections.
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Determines whether the enumerable is null or empty.
        /// </summary>
        /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
        /// <param name="source">The enumerable to check (can be null).</param>
        /// <returns>True if the enumerable is null or contains no elements; otherwise, false.</returns>
        /// <remarks>Uses .Any(), which will materialize the enumerable if it's lazy.</remarks>
        public static bool IsNullOrEmpty<T>(this IEnumerable<T>? source)
        {
            if (source == null)
                return true;

            return !source.Any();
        }
    }
}
