﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpDX;
using WoWEditor6.IO.Files.Models;
using WoWEditor6.Scene;
using WoWEditor6.Scene.Texture;

namespace WoWEditor6.IO.Files.Terrain.WoD
{
    class ChunkStreamInfo
    {
        public BinaryReader Stream;
        public int PosStart;
        public int Size;
    }

    class LoadedModel
    {
        public readonly string FileName;
        public readonly int Uuid;

        public LoadedModel(string file, int uuid)
        {
            FileName = file;
            Uuid = uuid;
        }
    }

    class MapArea : Terrain.MapArea
    {
        private Stream mMainStream;
        private Stream mTexStream;
        private Stream mObjStream;

        private BinaryReader mReader;
        private BinaryReader mTexReader;
        private BinaryReader mObjReader;

        private readonly List<string> mTextureNames = new List<string>();
        private readonly List<Graphics.Texture> mTextures = new List<Graphics.Texture>();
        private readonly List<float> mTextureScales = new List<float>();

        private readonly List<ChunkStreamInfo> mMainChunks = new List<ChunkStreamInfo>();
        private readonly List<ChunkStreamInfo> mTexChunks = new List<ChunkStreamInfo>();
        private readonly List<ChunkStreamInfo> mObjChunks = new List<ChunkStreamInfo>();

        private readonly List<MapChunk> mChunks = new List<MapChunk>();

        private readonly List<LoadedModel> mWmoInstances = new List<LoadedModel>();

        public MapArea(string continent, int ix, int iy)
        {
            Continent = continent;
            IndexX = ix;
            IndexY = iy;
        }

        public override bool Intersect(ref Ray ray, out Terrain.MapChunk chunk, out float distance)
        {
            distance = float.MaxValue;
            chunk = null;

            var mindistance = float.MaxValue;
            if (BoundingBox.Intersects(ref ray) == false)
                return false;

            Terrain.MapChunk chunkHit = null;
            var hasHit = false;
            foreach(var cnk in mChunks)
            {
                float dist;
                if (cnk.Intersect(ref ray, out dist) == false)
                    continue;

                hasHit = true;
                if (dist >= mindistance) continue;

                mindistance = dist;
                chunkHit = cnk;
            }

            chunk = chunkHit;
            distance = mindistance;
            return hasHit;
        }

        public string GetTextureName(int index)
        {
            if (index >= mTextureNames.Count)
                throw new IndexOutOfRangeException();

            return mTextureNames[index];
        }

        public float GetTextureScale(int index)
        {
            if (index >= mTextureScales.Count)
                throw new IndexOutOfRangeException();

            return mTextureScales[index];
        }

        public override Graphics.Texture GetTexture(int index)
        {
            if (index >= mTextures.Count)
                throw new IndexOutOfRangeException();

            return mTextures[index];
        }

        public override Terrain.MapChunk GetChunk(int index)
        {
            if (index >= mChunks.Count)
                throw new IndexOutOfRangeException();

            return mChunks[index];
        }

        public override void AsyncLoad()
        {
            try
            {
                mMainStream =
                    FileManager.Instance.Provider.OpenFile(string.Format(@"World\Maps\{0}\{0}_{1}_{2}.adt", Continent,
                        IndexX, IndexY));

                mTexStream = FileManager.Instance.Provider.OpenFile(string.Format(@"World\Maps\{0}\{0}_{1}_{2}_tex0.adt", Continent,
                        IndexX, IndexY));

                mObjStream = FileManager.Instance.Provider.OpenFile(string.Format(@"World\Maps\{0}\{0}_{1}_{2}_obj0.adt", Continent,
                        IndexX, IndexY));

                if (mMainStream == null || mTexStream == null || mObjStream == null)
                {
                    IsValid = false;
                    return;
                }

                mReader = new BinaryReader(mMainStream);
                mTexReader = new BinaryReader(mTexStream);
                mObjReader = new BinaryReader(mObjStream);

                InitChunkInfos();

                mTexStream.Position = 0;
                InitTextureNames();

                mObjStream.Position = 0;
                InitModels();

                InitChunks();
            }
            catch(Exception e)
            {
                Log.Warning(string.Format("Attempted to load ADT {0}_{1}_{2}.adt but it caused an error: {3}. Skipping the adt.", Continent, IndexX, IndexY, e.Message));
                IsValid = false;
                return;
            }

            IsValid = true;
        }

        private void InitChunkInfos()
        {
            for(var i = 0; i < 256; ++i)
            {
                if (SeekNextMcnk(mReader) == false)
                    throw new InvalidOperationException("Unable to read MCNK from adt");

                if (SeekNextMcnk(mTexReader) == false)
                    throw new InvalidOperationException("Unable to read MCNK from tex adt");

                if (SeekNextMcnk(mObjReader) == false)
                    throw new InvalidOperationException("Unable to read MCNK from obj adt");

                mMainChunks.Add(new ChunkStreamInfo
                {
                    PosStart = (int) mMainStream.Position,
                    Size = mReader.ReadInt32(),
                    Stream = mReader
                });

                mTexChunks.Add(new ChunkStreamInfo
                {
                    PosStart = (int)mTexStream.Position,
                    Size = mTexReader.ReadInt32(),
                    Stream = mTexReader
                });

                mObjChunks.Add(new ChunkStreamInfo
                {
                    PosStart = (int)mObjStream.Position,
                    Size = mObjReader.ReadInt32(),
                    Stream = mObjReader
                });

                mReader.ReadBytes(mMainChunks.Last().Size);
                mTexReader.ReadBytes(mTexChunks.Last().Size);
                mObjReader.ReadBytes(mObjChunks.Last().Size);
            }
        }

        private void InitModels()
        {
            //InitWmoModels();
            InitM2Models();
        }

        private void InitM2Models()
        {
            if (SeekChunk(mObjReader, 0x4D4D4458) == false)
                return;

            var size = mObjReader.ReadInt32();
            var bytes = mObjReader.ReadBytes(size);
            var fullString = Encoding.ASCII.GetString(bytes);
            var modelNames = fullString.Split('\0');
            var modelNameLookup = new Dictionary<int, string>();
            var curOffset = 0;
            foreach (var name in modelNames)
            {
                modelNameLookup.Add(curOffset, name);
                curOffset += name.Length + 1;
            }

            if (SeekChunk(mObjReader, 0x4D4D4944) == false)
                return;

            size = mObjReader.ReadInt32();
            var modelNameIds = mObjReader.ReadArray<int>(size / 4);

            if (SeekChunk(mObjReader, 0x4D444446) == false)
                return;

            size = mObjReader.ReadInt32();
            var mddf = mObjReader.ReadArray<Mddf>(size / SizeCache<Mddf>.Size);
            foreach(var entry in mddf)
            {
                if (entry.Mmid >= modelNameIds.Length)
                    continue;

                var nameId = modelNameIds[entry.Mmid];
                string modelName;
                if (modelNameLookup.TryGetValue(nameId, out modelName) == false)
                    continue;

                var position = new Vector3(entry.Position.X, 64.0f * Metrics.TileSize - entry.Position.Z,
                    entry.Position.Y);
                var rotation = new Vector3(360.0f - entry.Rotation.X, 360.0f - entry.Rotation.Z, entry.Rotation.Y - 90);
                var scale = entry.Scale / 1024.0f;

                var instance = WorldFrame.Instance.M2Manager.AddInstance(modelName, entry.UniqueId, position, rotation,
                    new Vector3(scale));

                DoodadInstances.Add(new M2Instance
                {
                    Hash = modelName.ToUpperInvariant().GetHashCode(),
                    Uuid = entry.UniqueId,
                    BoundingBox = instance?.BoundingBox ?? new BoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue)),
                    RenderInstance = instance
                });
            }
        }

        private void InitWmoModels()
        {
            if (SeekChunk(mObjReader, 0x4D574D4F) == false)
                return;

            var size = mObjReader.ReadInt32();
            var bytes = mObjReader.ReadBytes(size);
            var fullString = Encoding.ASCII.GetString(bytes);
            var modelNames = fullString.Split('\0');
            var modelNameLookup = new Dictionary<int, string>();
            var curOffset = 0;
            foreach(var name in modelNames)
            {
                modelNameLookup.Add(curOffset, name);
                curOffset += name.Length + 1;
            }

            if (SeekChunk(mObjReader, 0x4D574944) == false)
                return;

            size = mObjReader.ReadInt32();
            var modelNameIds = mObjReader.ReadArray<int>(size / 4);

            if (SeekChunk(mObjReader, 0x4D4F4446) == false)
                return;

            size = mObjReader.ReadInt32();
            var modf = mObjReader.ReadArray<Modf>(size / SizeCache<Modf>.Size);

            foreach(var entry in modf)
            {
                if (entry.Mwid >= modelNameIds.Length)
                    continue;

                var nameId = modelNameIds[entry.Mwid];
                string modelName;
                if (modelNameLookup.TryGetValue(nameId, out modelName) == false)
                    continue;

                var position = new Vector3(entry.Position.X, 64.0f * Metrics.TileSize - entry.Position.Z,
                    entry.Position.Y);
                var rotation = new Vector3(360.0f - entry.Rotation.X, 360.0f - entry.Rotation.Z, entry.Rotation.Y - 90);

                WorldFrame.Instance.WmoManager.AddInstance(modelName, entry.UniqueId, position, rotation);
                mWmoInstances.Add(new LoadedModel(modelName, entry.UniqueId));
            }
        }

        private void InitTextureNames()
        {
            if (SeekChunk(mTexReader, 0x4D544558) == false)
                return;

            var size = mTexReader.ReadInt32();
            var bytes = mTexReader.ReadBytes(size);
            var fullString = Encoding.ASCII.GetString(bytes);
            mTextureNames.AddRange(fullString.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries));
            mTextureNames.ForEach(t => mTextures.Add(TextureManager.Instance.GetTexture(t)));

            mTextureNames.ForEach(t =>
            {
                var loadInfo = Texture.TextureLoader.LoadHeaderOnly(t);
                var width = loadInfo?.Width ?? 256;
                var height = loadInfo?.Height ?? 256;
                if (width <= 256 || height <= 256 || loadInfo == null)
                    mTextureScales.Add(1.0f);
                else
                    mTextureScales.Add(256.0f / (2 * loadInfo.Width));
            });
        }

        private void InitChunks()
        {
            var minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            var modelMin = new Vector3(float.MaxValue);
            var modelMax = new Vector3(float.MinValue);

            for (var i = 0; i < 256; ++i)
            {
                var chunk = new MapChunk(mMainChunks[i], mTexChunks[i], mObjChunks[i], i % 16, i / 16, this);
                chunk.AsyncLoad();
                var bbmin = chunk.BoundingBox.Minimum;
                var bbmax = chunk.BoundingBox.Maximum;
                if (bbmin.X < minPos.X)
                    minPos.X = bbmin.X;
                if (bbmax.X > maxPos.X)
                    maxPos.X = bbmax.X;
                if (bbmin.Y < minPos.Y)
                    minPos.Y = bbmin.Y;
                if (bbmax.Y > maxPos.Y)
                    maxPos.Y = bbmax.Y;
                if (bbmin.Z < minPos.Z)
                    minPos.Z = bbmin.Z;
                if (bbmax.Z > maxPos.Z)
                    maxPos.Z = bbmax.Z;

                bbmin = chunk.ModelBox.Minimum;
                bbmax = chunk.ModelBox.Maximum;
                if (bbmin.X < modelMin.X)
                    modelMin.X = bbmin.X;
                if (bbmax.X > modelMax.X)
                    modelMax.X = bbmax.X;
                if (bbmin.Y < modelMin.Y)
                    modelMin.Y = bbmin.Y;
                if (bbmax.Y > modelMax.Y)
                    modelMax.Y = bbmax.Y;
                if (bbmin.Z < modelMin.Z)
                    modelMin.Z = bbmin.Z;
                if (bbmax.Z > modelMax.Z)
                    modelMax.Z = bbmax.Z;

                mChunks.Add(chunk);
                Array.Copy(chunk.Vertices, 0, FullVertices, i * 145, 145);
            }

            BoundingBox = new BoundingBox(minPos, maxPos);
            ModelBox = new BoundingBox(modelMin, modelMax);
        }

        private static bool SeekNextMcnk(BinaryReader reader) => SeekChunk(reader, 0x4D434E4B, false);

        private static bool SeekChunk(BinaryReader reader, uint signature, bool begin = true)
        {
            if (begin)
                reader.BaseStream.Position = 0;

            try
            {
                var sig = reader.ReadUInt32();
                while(sig != signature)
                {
                    var size = reader.ReadInt32();
                    reader.ReadBytes(size);
                    sig = reader.ReadUInt32();
                }

                return sig == signature;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public override void Dispose()
        {
            mMainStream?.Dispose();
            mTexStream?.Dispose();
            mObjStream?.Dispose();

            foreach (var chunk in mChunks)
                chunk.Dispose();

            mChunks.Clear();
            mTextures.Clear();

            foreach (var instance in mWmoInstances)
                WorldFrame.Instance.WmoManager.RemoveInstance(instance.FileName, instance.Uuid);

            foreach (var instance in DoodadInstances)
                WorldFrame.Instance.M2Manager.RemoveInstance(instance.Hash, instance.Uuid);

            mWmoInstances.Clear();
        }
    }
}
