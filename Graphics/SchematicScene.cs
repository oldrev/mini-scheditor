using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using MiniScheditor.Core;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public class SchematicScene
{
    private readonly ISpatialIndex<SchematicItem> _spatialIndex;
    private readonly List<SchematicItem> _items = new List<SchematicItem>();
    private readonly Dictionary<SchematicObject, SchematicItem> _modelToItemMap = new Dictionary<SchematicObject, SchematicItem>();

    public Rect32 SceneRect { get; }

    public SchematicScene(Rect32 sceneRect)
    {
        SceneRect = sceneRect;
        _spatialIndex = new QuadTree<SchematicItem>(0, sceneRect);
    }

    public void AddItem(SchematicItem item)
    {
        _items.Add(item);
        _spatialIndex.Insert(item);
        if (item.Model != null)
        {
            _modelToItemMap[item.Model] = item;
        }
    }

    public void RemoveItem(SchematicItem item)
    {
        _items.Remove(item);
        _spatialIndex.Remove(item);
        if (item.Model != null)
        {
            _modelToItemMap.Remove(item.Model);
        }
    }

    public void Clear()
    {
        _items.Clear();
        _spatialIndex.Clear();
        _modelToItemMap.Clear();
    }

    public void Draw(DrawingContext context, Rect32 visibleRect, double scale)
    {
        var visibleItems = new List<SchematicItem>();
        _spatialIndex.Query(visibleRect, visibleItems);

        foreach (var item in visibleItems)
        {
            item.Draw(context, scale);
        }
    }

    public SchematicItem? GetItemForModel(SchematicObject model)
    {
        _modelToItemMap.TryGetValue(model, out var item);
        return item;
    }

    public static SchematicItem CreateItem(SchematicObject obj)
    {
        return obj switch
        {
            Component c => new ComponentItem(c),
            Wire w => new WireItem(w),
            Junction j => new JunctionItem(j),
            _ => throw new System.ArgumentException($"Unknown object type: {obj.GetType().Name}")
        };
    }
    
    // Helper to sync from a Document (optional, but useful for migration)
    public void SyncFromDocument(SchematicDocument document)
    {
        Clear();
        foreach (var layer in document.Layers)
        {
            foreach (var obj in layer.Objects)
            {
                AddItem(CreateItem(obj));
            }
        }
    }
}
