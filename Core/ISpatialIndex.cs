using System.Collections.Generic;

namespace MiniScheditor.Core;

public interface ISpatialIndex<T> where T : ISpatialObject
{
    void Insert(T item);
    bool Remove(T item);
    void Clear();
    void Query(in Rect32 area, List<T> results);
}
