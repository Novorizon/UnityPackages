//   Node.java
//   Java Spatial Index Library
//   Copyright (C) 2002 Infomatiq Limited
//  
//  This library is free software; you can redistribute it and/or
//  modify it under the terms of the GNU Lesser General Public
//  License as published by the Free Software Foundation; either
//  version 2.1 of the License, or (at your option) any later version.
//  
//  This library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
//  Lesser General Public License for more details.
//  
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

//  Ported to C# By Dror Gluska, April 9th, 2009
//  Updated by Boris Kudryashov, January 29, 2014
namespace RTree
{

    //import com.infomatiq.jsi.Rectangle;

    /**
     * <p>Used by RTree. There are no public methods in this class.</p>
     * 
     * @author aled@sourceforge.net
     * @version 1.0b2p1
     */
    /// <summary>提供节点的实现。</summary>
    public class Node<T>
    {
        internal int nodeId = 0;
        internal Rectangle mbr = null;
        internal Rectangle[] entries = null;
        internal int[] ids = null;
        internal int level;
        internal int entryCount;

        /// <summary>使用调用方提供的nodeId、level、maxNodeEntries创建 Node。</summary>
        public Node(int nodeId, int level, int maxNodeEntries)
        {
            this.nodeId = nodeId;
            this.level = level;
            entries = new Rectangle[maxNodeEntries];
            ids = new int[maxNodeEntries];
        }

        /// <summary>调用 r.copy 完成add 条目。</summary>
        internal void addEntry(Rectangle r, int id)
        {
            ids[entryCount] = id;
            entries[entryCount] = r.copy();
            entryCount++;
            if (mbr == null)
            {
                mbr = r.copy();
            }
            else
            {
                mbr.add(r);
            }
        }

        /// <summary>调用 r.copy 完成add 条目 NoCopy。</summary>
        internal void addEntryNoCopy(Rectangle r, int id)
        {
            ids[entryCount] = id;
            entries[entryCount] = r;
            entryCount++;
            if (mbr == null)
            {
                mbr = r.copy();
            }
            else
            {
                mbr.add(r);
            }
        }

        // Return the index of the found entry, or -1 if not found
        /// <summary>根据传入参数取得find 条目。</summary>
        internal int findEntry(Rectangle r, int id)
        {
            for (int i = 0; i < entryCount; i++)
            {
                if (id == ids[i] && sameBounds(r, entries[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>返回same 包围盒的判断结果。</summary>
        private static bool sameBounds(Rectangle left, Rectangle right)
        {
            // Rectangle subclasses may define their own object equality. The
            // index matches geometric bounds, including copies stored as base boxes.
            for (int d = 0; d < Rectangle.DIMENSIONS; d++)
                if (left.min[d] != right.min[d] || left.max[d] != right.max[d]) return false;
            return true;
        }

        // delete entry. This is done by setting it to null and copying the last entry into its space.
        /// <summary>调用 recalculateMBR 完成delete 条目。</summary>
        internal void deleteEntry(int i, int minNodeEntries)
        {
            int lastIndex = entryCount - 1;
            Rectangle deletedRectangle = entries[i];
            entries[i] = null;
            if (i != lastIndex)
            {
                entries[i] = entries[lastIndex];
                ids[i] = ids[lastIndex];
                entries[lastIndex] = null;
            }
            ids[lastIndex] = 0;
            entryCount--;

            // The root may remain below the minimum occupancy. Keep every
            // surviving node's bounds tight, including an empty root.
            recalculateMBR(deletedRectangle);
        }

        // oldRectangle is a rectangle that has just been deleted or made smaller.
        // Thus, the MBR is only recalculated if the OldRectangle influenced the old MBR
        /// <summary>调用 mbr.edgeOverlaps 完成recalculateMBR。</summary>
        internal void recalculateMBR(Rectangle deletedRectangle)
        {
            if (entryCount == 0)
            {
                mbr = null;
                return;
            }
            if (mbr.edgeOverlaps(deletedRectangle))
            {
                mbr.set(entries[0].min, entries[0].max);

                for (int i = 1; i < entryCount; i++)
                {
                    mbr.add(entries[i]);
                }
            }
        }

        /// <summary>通过当前接口读取条目数量。</summary>
        public int getEntryCount()
        {
            return entryCount;
        }

        /// <summary>通过当前接口读取条目。</summary>
        public Rectangle getEntry(int index)
        {
            if (index < entryCount)
            {
                return entries[index];
            }
            return null;
        }

        /// <summary>通过当前接口读取标识。</summary>
        public int getId(int index)
        {
            if (index < entryCount)
            {
                return ids[index];
            }
            return -1;
        }

        /**
         * eliminate null entries, move all entries to the start of the source node
         */
        /// <summary>处理reorganize。</summary>
        internal void reorganize(RTree<T> rtree)
        {
            int countdownIndex = rtree.maxNodeEntries - 1;
            for (int index = 0; index < entryCount; index++)
            {
                if (entries[index] == null)
                {
                    while (entries[countdownIndex] == null && countdownIndex > index)
                    {
                        countdownIndex--;
                    }
                    entries[index] = entries[countdownIndex];
                    ids[index] = ids[countdownIndex];
                    entries[countdownIndex] = null;
                }
            }
        }

        /// <summary>返回is 叶节点的判断结果。</summary>
        internal bool isLeaf()
        {
            return (level == 1);
        }

        /// <summary>通过当前接口读取等级。</summary>
        public int getLevel()
        {
            return level;
        }

        /// <summary>通过当前接口读取MBR。</summary>
        public Rectangle getMBR()
        {
            return mbr;
        }
    }

}
