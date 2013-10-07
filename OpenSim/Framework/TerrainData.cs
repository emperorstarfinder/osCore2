/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;

using OpenMetaverse;

namespace OpenSim.Framework
{
    public abstract class TerrainData
    {
        // Terrain always is a square
        public int SizeX { get; protected set; }
        public int SizeY { get; protected set; }
        public int SizeZ { get; protected set; }

        public abstract float this[int x, int y] { get; set; }
        // Someday terrain will have caves
        public abstract float this[int x, int y, int z] { get; set; }

        public bool IsTainted { get; protected set; }
        public abstract bool IsTaintedAt(int xx, int yy);
        public abstract void ClearTaint();

        // Return a representation of this terrain for storing as a blob in the database.
        // Returns 'true' to say blob was stored in the 'out' locations.
        public abstract bool GetDatabaseBlob(out int DBFormatRevisionCode, out Array blob);

        // return a special compressed representation of the heightmap in shorts
        public abstract short[] GetCompressedMap();
        public abstract void SetCompressedMap(short[] cmap);

        public abstract TerrainData Clone();
    }

    // The terrain is stored as a blob in the database with a 'revision' field.
    // Some implementations of terrain storage would fill the revision field with
    //    the time the terrain was stored. When real revisions were added and this
    //    feature removed, that left some old entries with the time in the revision
    //    field.
    // Thus, if revision is greater than 'RevisionHigh' then terrain db entry is
    //    left over and it is presumed to be 'Legacy256'.
    // Numbers are arbitrary and are chosen to to reduce possible mis-interpretation.
    // If a revision does not match any of these, it is assumed to be Legacy256.
    public enum DBTerrainRevision
    {
        // Terrain is 'double[256,256]'
        Legacy256 = 11,
        // Terrain is 'int32, int32, float[,]' where the shorts are X and Y dimensions
        // The dimensions are presumed to be multiples of 16 and, more likely, multiples of 256.
        Variable2D = 22,
        // A revision that is not listed above or any revision greater than this value is 'Legacy256'.
        RevisionHigh = 1234
    }

    // Version of terrain that is a heightmap.
    // This should really be 'LLOptimizedHeightmapTerrainData' as it includes knowledge
    //    of 'patches' which are 16x16 terrain areas which can be sent separately to the viewer.
    // The heighmap is kept as an array of short integers. The integer values are converted to
    //    and from floats by TerrainCompressionFactor.
    public class HeightmapTerrainData : TerrainData
    {
        // TerrainData.this[x, y]
        public override float this[int x, int y]
        {
            get { return FromCompressedHeight(m_heightmap[x, y]); }
            set {
                short newVal = ToCompressedHeight(value);
                if (m_heightmap[x, y] != newVal)
                {
                    m_heightmap[x, y] = newVal;
                    m_taint[x / Constants.TerrainPatchSize, y / Constants.TerrainPatchSize] = true;

                }
            }
        }

        // TerrainData.this[x, y, z]
        public override float this[int x, int y, int z]
        {
            get { return this[x, y]; }
            set { this[x, y] = value; }
        }

        // TerrainData.ClearTaint
        public override void ClearTaint()
        {
            IsTainted = false;
            for (int ii = 0; ii < m_taint.GetLength(0); ii++)
                for (int jj = 0; jj < m_taint.GetLength(1); jj++)
                    m_taint[ii, jj] = false;
        }

        public override bool IsTaintedAt(int xx, int yy)
        {
            return m_taint[xx / Constants.TerrainPatchSize, yy / Constants.TerrainPatchSize];
        }

        // TerrainData.GetDatabaseBlob
        // The user wants something to store in the database.
        public override bool GetDatabaseBlob(out int DBRevisionCode, out Array blob)
        {
            DBRevisionCode = (int)DBTerrainRevision.Legacy256;
            blob = LegacyTerrainSerialization();
            return false;
        }

        public override short[] GetCompressedMap()
        {
            short[] newMap = new short[SizeX * SizeY];

            int ind = 0;
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    newMap[ind++] = m_heightmap[xx, yy];

            return newMap;

        }
        public override void SetCompressedMap(short[] cmap)
        {
            int ind = 0;
            for (int xx = 0; xx < SizeX; xx++)
                for (int yy = 0; yy < SizeY; yy++)
                    m_heightmap[xx, yy] = cmap[ind++];
        }

        // TerrainData.Clone
        public override TerrainData Clone()
        {
            HeightmapTerrainData ret = new HeightmapTerrainData(SizeX, SizeY, SizeZ);
            ret.m_heightmap = (short[,])this.m_heightmap.Clone();
            return ret;
        }

        // =============================================================

        private short[,] m_heightmap;
        // Remember subregions of the heightmap that has changed.
        private bool[,] m_taint;

        // To save space (especially for large regions), keep the height as a short integer
        //    that is coded as the float height times the compression factor (usually '100'
        //    to make for two decimal points).
        public static short ToCompressedHeight(double pHeight)
        {
            return (short)(pHeight * Constants.TerrainCompression);
        }

        public static float FromCompressedHeight(short pHeight)
        {
            return ((float)pHeight) / Constants.TerrainCompression;
        }

        // To keep with the legacy theme, this can be created with the way terrain
        //     used to passed around as.
        public HeightmapTerrainData(double[,] pTerrain)
        {
            SizeX = pTerrain.GetLength(0);
            SizeY = pTerrain.GetLength(1);
            SizeZ = (int)Constants.RegionHeight;

            m_heightmap = new short[SizeX, SizeY];
            for (int ii = 0; ii < SizeX; ii++)
            {
                for (int jj = 0; jj < SizeY; jj++)
                {
                    m_heightmap[ii, jj] = ToCompressedHeight(pTerrain[ii, jj]);

                }
            }

            m_taint = new bool[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];
            ClearTaint();
        }

        // Create underlying structures but don't initialize the heightmap assuming the caller will immediately do that
        public HeightmapTerrainData(int pX, int pY, int pZ)
        {
            SizeX = pX;
            SizeY = pY;
            SizeZ = pZ;
            m_heightmap = new short[SizeX, SizeY];
            m_taint = new bool[SizeX / Constants.TerrainPatchSize, SizeY / Constants.TerrainPatchSize];
            ClearTaint();
        }

        public HeightmapTerrainData(short[] cmap, int pX, int pY, int pZ) : this(pX, pY, pZ)
        {
            SetCompressedMap(cmap);
        }


        // Just create an array of doubles. Presumes the caller implicitly knows the size.
        public Array LegacyTerrainSerialization()
        {
            Array ret = null;
            using (MemoryStream str = new MemoryStream(SizeX * SizeY * sizeof(double)))
            {
                using (BinaryWriter bw = new BinaryWriter(str))
                {
                    // TODO: COMPATIBILITY - Add byte-order conversions
                    for (int ii = 0; ii < SizeX; ii++)
                        for (int jj = 0; jj < SizeY; jj++)
                    {
                        double height = this[ii, jj];
                        if (height == 0.0)
                            height = double.Epsilon;
                        bw.Write(height);
                    }
                }
                ret = str.ToArray();
            }
            return ret;
        }
    }
}