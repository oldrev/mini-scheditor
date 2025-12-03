using System;
using System.Collections.Generic;

namespace MiniScheditor.Core
{
    public class QuadTree<T> : ISpatialIndex<T> where T : ISpatialObject
    {
        private const int MAX_OBJECTS = 10;
        private const int MAX_LEVELS = 12;

        private int _level;
        private List<T> _objects;
        private Rect32 _bounds;
        private QuadTree<T>?[] _nodes;

        public QuadTree(int pLevel, Rect32 pBounds)
        {
            _level = pLevel;
            _objects = new List<T>();
            _bounds = pBounds;
            _nodes = new QuadTree<T>?[4];
        }

        public void Clear()
        {
            _objects.Clear();
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i] != null)
                {
                    _nodes[i]!.Clear();
                    _nodes[i] = null;
                }
            }
        }

        private void Split()
        {
            int subWidth = (int)(_bounds.Width / 2);
            int subHeight = (int)(_bounds.Height / 2);
            int x = _bounds.X;
            int y = _bounds.Y;

            _nodes[0] = new QuadTree<T>(_level + 1, new Rect32(x + subWidth, y, subWidth, subHeight));
            _nodes[1] = new QuadTree<T>(_level + 1, new Rect32(x, y, subWidth, subHeight));
            _nodes[2] = new QuadTree<T>(_level + 1, new Rect32(x, y + subHeight, subWidth, subHeight));
            _nodes[3] = new QuadTree<T>(_level + 1, new Rect32(x + subWidth, y + subHeight, subWidth, subHeight));
        }

        private int GetIndex(in Rect32 pRect)
        {
            int index = -1;
            double verticalMidpoint = _bounds.X + (_bounds.Width / 2.0);
            double horizontalMidpoint = _bounds.Y + (_bounds.Height / 2.0);

            bool topQuadrant = (pRect.Y < horizontalMidpoint && pRect.Y + pRect.Height < horizontalMidpoint);
            bool bottomQuadrant = (pRect.Y > horizontalMidpoint);

            if (pRect.X < verticalMidpoint && pRect.X + pRect.Width < verticalMidpoint)
            {
                if (topQuadrant)
                {
                    index = 1;
                }
                else if (bottomQuadrant)
                {
                    index = 2;
                }
            }
            else if (pRect.X > verticalMidpoint)
            {
                if (topQuadrant)
                {
                    index = 0;
                }
                else if (bottomQuadrant)
                {
                    index = 3;
                }
            }

            return index;
        }

        public void Insert(T pObject)
        {
            if (_nodes[0] != null)
            {
                int index = GetIndex(pObject.Bounds);

                if (index != -1)
                {
                    _nodes[index]!.Insert(pObject);
                    return;
                }
            }

            _objects.Add(pObject);

            if (_objects.Count > MAX_OBJECTS && _level < MAX_LEVELS)
            {
                if (_nodes[0] == null)
                {
                    Split();
                }

                int i = 0;
                while (i < _objects.Count)
                {
                    int index = GetIndex(_objects[i].Bounds);
                    if (index != -1)
                    {
                        T obj = _objects[i];
                        _objects.RemoveAt(i);
                        _nodes[index]!.Insert(obj);
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        public bool Remove(T pObject)
        {
            if (_nodes[0] != null)
            {
                int index = GetIndex(pObject.Bounds);
                if (index != -1)
                {
                    return _nodes[index]!.Remove(pObject);
                }
            }

            return _objects.Remove(pObject);
        }

        public List<T> Retrieve(List<T> returnObjects, in Rect32 pRect)
        {
            int index = GetIndex(pRect);
            if (index != -1 && _nodes[0] != null)
            {
                _nodes[index]!.Retrieve(returnObjects, pRect);
            }
            else if (_nodes[0] != null)
            {
                // If the rect doesn't fit into a subnode, it might overlap multiple.
                // We need to check all nodes that intersect.
                // The GetIndex optimization is for insertion mostly.
                // For retrieval, simple intersection check is safer.
                foreach (var node in _nodes)
                {
                    if (node != null && node._bounds.Intersects(pRect))
                    {
                        node.Retrieve(returnObjects, pRect);
                    }
                }
            }

            // Add objects from this node that intersect
            foreach (var obj in _objects)
            {
                if (obj.Bounds.Intersects(pRect))
                {
                    returnObjects.Add(obj);
                }
            }

            return returnObjects;
        }

        // Simplified retrieve that just checks intersection with all subnodes if index logic fails
        public void Query(in Rect32 area, List<T> results)
        {
            if (!_bounds.Intersects(area))
                return;

            foreach (var obj in _objects)
            {
                if (obj.Bounds.Intersects(area))
                    results.Add(obj);
            }

            if (_nodes[0] != null)
            {
                foreach (var node in _nodes)
                {
                    node?.Query(area, results);
                }
            }
        }
    }
}
