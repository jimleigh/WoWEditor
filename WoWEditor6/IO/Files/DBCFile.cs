﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WoWEditor6.IO.Files
{
    unsafe class DbcRecord
    {
        private readonly int mSize;
        private readonly byte[] mData;
        private readonly Dictionary<int, string> mStringTable;

        private void AssertValid(int offset, int size)
        {
            if (offset + size * 4 > mSize)
                throw new IndexOutOfRangeException("Trying to read past the end of a dbc record");
        }

        public DbcRecord(int size, int offset, BinaryReader reader, Dictionary<int, string> stringTable)
        {
            mSize = size;
            mData = new byte[size];
            reader.BaseStream.Position = offset;
            reader.BaseStream.Read(mData, 0, mSize);
            mStringTable = stringTable;
        }

        public int GetInt32(int field)
        {
            AssertValid(field * 4, 1);
            fixed(byte* ptr = mData)
                return *(int*) (ptr + field * 4);
        }

        public uint GetUint32(int field)
        {
            AssertValid(field * 4, 1);
            fixed (byte* ptr = mData)
                return *(uint*) (ptr + field * 4);
        }

        public float GetFloat(int field)
        {
            AssertValid(field * 4, 1);
            fixed (byte* ptr = mData)
                return *(float*)(ptr + field * 4);
        }

        public string GetString(int field)
        {
            int offset;
            fixed (byte* ptr = mData)
                offset = *(int*) (ptr + field * 4);

            if (offset == 0)
                return null;

            string str;
            mStringTable.TryGetValue(offset, out str);
            return str;    
        }
    }

    class DbcFile : IDisposable
    {
        private int mRecordSize;
        private int mStringTableSize;
        private Stream mStream;
        private BinaryReader mReader;
        private readonly Dictionary<int, string> mStringTable = new Dictionary<int, string>();

        public int NumRows { get; private set; }
        public int NumFields { get; private set; }

        public void Load(string file)
        {
            mStream = FileManager.Instance.Provider.OpenFile(file);
            if (mStream == null)
                throw new FileNotFoundException(file);

            mReader = new BinaryReader(mStream);
            mReader.ReadInt32(); // signature
            NumRows = mReader.ReadInt32();
            NumFields = mReader.ReadInt32();
            mRecordSize = mReader.ReadInt32();
            mStringTableSize = mReader.ReadInt32();

            mStream.Position = NumRows * mRecordSize + 20;
            var strBytes = mReader.ReadBytes(mStringTableSize);
            var curOffset = 0;
            var curBytes = new List<byte>();
            for(var i = 0; i < strBytes.Length; ++i)
            {
                if (strBytes[i] != 0)
                {
                    curBytes.Add(strBytes[i]);
                    continue;
                }

                mStringTable.Add(curOffset, Encoding.UTF8.GetString(curBytes.ToArray()));
                curBytes.Clear();
                curOffset = i + 1;
            }
        }

        public DbcRecord GetRow(int index)
        {
            return new DbcRecord(mRecordSize, 20 + index * mRecordSize, mReader, mStringTable);
        }

        public void Dispose()
        {
            mStream?.Dispose();
        }
    }
}