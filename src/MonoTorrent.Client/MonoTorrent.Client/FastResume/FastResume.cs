//
// FastResume.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2009 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.IO;

using MonoTorrent.BEncoding;

namespace MonoTorrent.Client
{
    public class FastResume
    {
        // Version 1 stored the Bitfield and Infohash.
        //
        // Version 2 added the UnhashedPieces bitfield.
        //
        static readonly BEncodedNumber FastResumeVersion = 2;

        internal static readonly BEncodedString BitfieldKey = "bitfield";
        internal static readonly BEncodedString BitfieldLengthKey = "bitfield_length";
        internal static readonly BEncodedString InfoHashKey = "infohash";
        internal static readonly BEncodedString UnhashedPiecesKey = "unhashed_pieces";
        internal static readonly BEncodedString VersionKey = "version";

        public BitField Bitfield { get; }

        public InfoHashes InfoHashes { get; }

        public BitField UnhashedPieces { get; }

        internal FastResume (InfoHashes infoHashes, BitField bitfield, BitField unhashedPieces)
        {
            InfoHashes = infoHashes ?? throw new ArgumentNullException (nameof (infoHashes));
            Bitfield = new BitField (bitfield);
            UnhashedPieces = new BitField (unhashedPieces);

            for (int i = 0; i < Bitfield.Length; i++) {
                if (bitfield[i] && unhashedPieces[i])
                    throw new ArgumentException ($"The bitfield is set to true at index {i} but that piece is marked as unhashed.");
            }
        }

        internal FastResume (BEncodedDictionary dict)
        {
            CheckVersion (dict);
            CheckContent (dict, InfoHashKey);
            CheckContent (dict, BitfieldKey);
            CheckContent (dict, BitfieldLengthKey);

            // BEP52: Support backwards/forwards compatibility
            var infoHash = InfoHash.FromMemory (((BEncodedString) dict[InfoHashKey]).AsMemory ());
            if (infoHash.Span.Length == 20)
                InfoHashes = InfoHashes.FromV1 (infoHash);
            else
                InfoHashes = InfoHashes.FromV2 (infoHash);

            var data = ((BEncodedString) dict[BitfieldKey]).Span;
            Bitfield = new BitField (data, (int) ((BEncodedNumber) dict[BitfieldLengthKey]).Number);

            // If we're loading up an older version of the FastResume data then we
            if (dict.ContainsKey (UnhashedPiecesKey)) {
                data = ((BEncodedString) dict[UnhashedPiecesKey]).Span;
                UnhashedPieces = new BitField (data, Bitfield.Length);
            } else {
                UnhashedPieces = new BitField (Bitfield.Length);
            }
        }

        static void CheckContent (BEncodedDictionary dict, BEncodedString key)
        {
            if (!dict.ContainsKey (key))
                throw new TorrentException ($"Invalid FastResume data. Key '{key}' was not present");
        }

        static void CheckVersion (BEncodedDictionary dict)
        {
            long? version = (dict[VersionKey] as BEncodedNumber)?.Number;
            if (version.GetValueOrDefault () == 1 || version.GetValueOrDefault () == 2)
                return;

            throw new ArgumentException ($"This FastResume is version {version}, but only version  '1' and '2' are supported");
        }

        public byte[] Encode ()
        {
            return new BEncodedDictionary {
                { VersionKey, FastResumeVersion },
                { InfoHashKey, new BEncodedString(InfoHashes.V1OrV2.Span.ToArray ()) },
                { BitfieldKey, new BEncodedString(Bitfield.ToByteArray()) },
                { BitfieldLengthKey, (BEncodedNumber)Bitfield.Length },
                { UnhashedPiecesKey, new BEncodedString (UnhashedPieces.ToByteArray ()) }
            }.Encode ();
        }

        public void Encode (Stream s)
        {
            byte[] data = Encode ();
            s.Write (data, 0, data.Length);
        }

        public static bool TryLoad (Stream s, out FastResume fastResume)
        {
            fastResume = Load (s);
            return fastResume != null;
        }

        public static bool TryLoad (string fastResumeFilePath, out FastResume fastResume)
        {
            fastResume = null;
            try {
                if (File.Exists (fastResumeFilePath)) {
                    using (FileStream s = File.Open (fastResumeFilePath, FileMode.Open)) {
                        fastResume = Load (s);
                    }
                }
            } catch {
            }
            return fastResume != null;
        }

        static FastResume Load (Stream s)
        {
            try {
                var data = (BEncodedDictionary) BEncodedDictionary.Decode (s);
                return new FastResume (data);
            } catch {
            }
            return null;
        }
    }
}
