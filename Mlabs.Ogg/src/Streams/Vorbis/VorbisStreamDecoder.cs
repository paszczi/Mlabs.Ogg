﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mlabs.Ogg.Container;

namespace Mlabs.Ogg.Streams.Vorbis
{
    public class VorbisStreamDecoder : StreamDecoder
    {
        private const int IdHeader = 0;
        private const int CommentHeader = 1;
        private const int SetupHeader = 2;

        public VorbisStreamDecoder(Stream stream) : base(stream)
        {
        }


        public override bool TryDecode(IList<Page> pages, IList<Packet> packets, out OggStream stream)
        {
            stream = null;
            //we need a minimum of free packets for id, comment and setup header
            if (packets.Count < 3)
                return false;

            if (!IsVorbisStream(packets[0], packets[1], packets[2]))
                return false;

            stream = Decode(pages, packets);
            return true;
        }

        private bool IsVorbisStream(Packet idHeader, Packet commentHeader, Packet setupHeader)
        {
            if (idHeader.Size < VorbisHeaderInfo.PacketHeaderSize)
                return false;
            if (commentHeader.Size < VorbisHeaderInfo.PacketHeaderSize)
                return false;
            if (setupHeader.Size < VorbisHeaderInfo.PacketHeaderSize)
                return false;

            return IsCorrectHeader(idHeader, VorbisHeaderInfo.IdHeaderType) &&
                   IsCorrectHeader(commentHeader, VorbisHeaderInfo.CommentHeaderType) &&
                   IsCorrectHeader(setupHeader, VorbisHeaderInfo.SetupHeaderType);
        }


        private bool IsCorrectHeader(Packet headerPacket, byte headerType)
        {
            byte[] header = Read(headerPacket.FileOffset, VorbisHeaderInfo.PacketHeaderSize);
            if (header[VorbisHeaderInfo.HeaderTypeIndex] != headerType)
                return false;
            string magicSeq = Encoding.ASCII.GetString(header, VorbisHeaderInfo.MagicSeqIndex, VorbisHeaderInfo.MagicSeqSize);
            if (magicSeq != VorbisHeaderInfo.MagicSeq)
                return false;
            return true;
        }


        private OggStream Decode(IList<Page> pages, IList<Packet> packets)
        {
            VorbisStream vorbis = new VorbisStream(pages);
            ParseIdHeder(vorbis, packets[IdHeader]);
            ParseCommentHeader(vorbis, packets[CommentHeader]);
            vorbis.Duration = GetDuration(pages, packets, vorbis.SampleRate);
            return vorbis;
        }


        private void ParseCommentHeader(VorbisStream vorbis, Packet packet)
        {
            FileStream.Seek(packet.FileOffset + VorbisHeaderInfo.VendorLengthIndex, SeekOrigin.Begin);
            uint vendorLength = BitConverter.ToUInt32(ReadNoSeek(4), 0);
            var vendorString = Encoding.UTF8.GetString(ReadNoSeek((int) vendorLength), 0, (int) vendorLength);
            
            uint userCommentListLength = BitConverter.ToUInt32(ReadNoSeek(4), 0);
            IList<string> userComments = new List<string>((int) userCommentListLength);
            for (uint i = 0; i < userCommentListLength; i++)
            {
                uint length = BitConverter.ToUInt32(ReadNoSeek(4), 0);
                string userComment = Encoding.UTF8.GetString(ReadNoSeek((int) length), 0, (int) length);
                userComments.Add(userComment);
            }
            bool framingFlag = BitConverter.ToBoolean(ReadNoSeek(1), 0);
            if (!framingFlag)
                throw new InvalidStreamException("Framing flag at the end of the comment header is not set");
            vorbis.Comments = new VorbisComments(vendorString, userComments);
        }


        private void ParseIdHeder(VorbisStream vorbisStream, Packet idHeader)
        {
            byte[] identificationHeader = Read(idHeader.FileOffset, idHeader.Size);
            byte version = identificationHeader[VorbisHeaderInfo.VersionIndex];
            byte audioChannels = identificationHeader[VorbisHeaderInfo.AudioChannelsIndex];
            uint audioSampleRate = BitConverter.ToUInt32(identificationHeader, VorbisHeaderInfo.AudioSampleRateIndex);
            int maxBitrate = BitConverter.ToInt32(identificationHeader, VorbisHeaderInfo.MaximumBitrateIndex);
            int nominalBitrate = BitConverter.ToInt32(identificationHeader, VorbisHeaderInfo.NominalBitrateIndex);
            int minBitrate = BitConverter.ToInt32(identificationHeader, VorbisHeaderInfo.MinimumBitrateIndex);
            uint blockSize0 = (uint) Math.Pow(2, (identificationHeader[VorbisHeaderInfo.BlockSizeIndex] & VorbisHeaderInfo.BlockSize0Mask));
            uint blockSize1 = (uint) Math.Pow(2, identificationHeader[VorbisHeaderInfo.BlockSizeIndex] >> 4);
            byte framingFlag = identificationHeader[VorbisHeaderInfo.FramingFlagIndex];

            vorbisStream.Version = version;
            vorbisStream.AudioChannels = audioChannels;
            vorbisStream.SampleRate = audioSampleRate;
            vorbisStream.BlockSize0 = blockSize0;
            vorbisStream.BlockSize1 = blockSize1;
            vorbisStream.FramingFlag = framingFlag;
            vorbisStream.MaxBitrate = maxBitrate;
            vorbisStream.MinBitrate = minBitrate;
            vorbisStream.NominalBitrate = nominalBitrate;
        }


        private TimeSpan GetDuration(IList<Page> pages, IList<Packet> packets, uint audioSampleRate)
        {
            //this is the first audio packet
            var packet = packets[SetupHeader + 1];
            var first = pages[packet.FirstPage];
            var last = pages.LastOrDefault(p => p.PageType == PageType.EndOfStream);

            ulong granuleDelta = last.GranulePosition - first.GranulePosition;
            double seconds = (double) granuleDelta/audioSampleRate;
            return TimeSpan.FromSeconds(seconds);
        }
    }
}