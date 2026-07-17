using System;
using System.Collections.Generic;

namespace CK3MPS
{
    internal static class WorkflowRuntimeLinqCompat
    {
        public static bool Any<T>(this IEnumerable<T> source)
        {
            if (source == null)
                return false;

            using (IEnumerator<T> enumerator = source.GetEnumerator())
                return enumerator.MoveNext();
        }

        public static bool Any<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null || predicate == null)
                return false;

            foreach (T item in source)
                if (predicate(item))
                    return true;

            return false;
        }
    }
}
