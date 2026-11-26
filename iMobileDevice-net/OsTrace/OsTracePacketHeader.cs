// <copyright file="OsTracePacketHeader.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591
#pragma warning disable 1572
#pragma warning disable 1573

namespace iMobileDevice.OsTrace
{
    using System.Runtime.InteropServices;

    /// <summary>
    /// Packet header for OS trace messages (<c>struct ostrace_packet_header_t</c>, packed to 1-byte alignment, 0x81 bytes).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct OsTracePacketHeader
    {
        public byte marker;

        public uint type;

        public uint header_size;

        public uint pid;

        public ulong procid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] procuuid;

        public ushort procpath_len;

        public ulong aid;

        public ulong paid;

        public ulong time_sec;

        public uint time_usec;

        public byte unk06;

        public byte level;

        public byte unk07;

        public byte unk08;

        public byte unk09;

        public byte unk10;

        public byte unk11;

        public byte unk12;

        public ulong timestamp;

        public uint thread_id;

        public uint unk13;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] imageuuid;

        public ushort imagepath_len;

        public uint message_len;

        public uint offset;

        public ushort subsystem_len;

        public ushort unk14;

        public ushort category_len;

        public ushort unk15;

        public uint unk16;
    }
}
