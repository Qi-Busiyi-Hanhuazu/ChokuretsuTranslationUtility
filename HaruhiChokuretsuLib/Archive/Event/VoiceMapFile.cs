﻿using HaruhiChokuretsuLib.Audio.ADX;
using HaruhiChokuretsuLib.Font;
using HaruhiChokuretsuLib.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HaruhiChokuretsuLib.Archive.Event
{
    /// <summary>
    /// A custom file built for the translation that maps subtitle text to voice files
    /// </summary>
    public class VoiceMapFile : EventFile
    {
        /// <summary>
        /// Offset of voice map entries
        /// </summary>
        public int VoiceMapEntriesSectionOffset { get; set; }
        /// <summary>
        /// Pointer to dialogue lines
        /// </summary>
        public int DialogueLinesPointer { get; set; }
        /// <summary>
        /// List of voice map entries
        /// </summary>
        public List<VoiceMapEntry> VoiceMapEntries { get; set; } = [];
        /// <summary>
        /// Path to where voice files are contained; used only to get timings in NewFile()
        /// </summary>
        public string VceDirPath { get; set; }

        /// <inheritdoc/>
        public override void Initialize(byte[] decompressedData, int offset, ILogger log)
        {
            Log = log;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Offset = offset;
            Data = [.. decompressedData];

            int numFrontPointers = BitConverter.ToInt32(decompressedData.Take(4).ToArray());
            for (int i = 0; i < numFrontPointers; i++)
            {
                EventFileSections.Add(new()
                {
                    Pointer = BitConverter.ToInt32(decompressedData.Skip(0x0C + 0x08 * i).Take(4).ToArray()),
                    ItemCount = BitConverter.ToInt32(decompressedData.Skip(0x10 + 0x08 * i).Take(4).ToArray()),
                });
            }
            Settings = new(new byte[0x128]);
            VoiceMapEntriesSectionOffset = EventFileSections[0].Pointer;
            Settings.DialogueSectionPointer = EventFileSections[1].Pointer;
            DialogueLinesPointer = Settings.DialogueSectionPointer + (EventFileSections.Count - 1) * 12;
            Settings.NumDialogueEntries = numFrontPointers - 2;

            for (int i = 2; i < EventFileSections.Count; i++)
            {
                DramatisPersonae.Add(EventFileSections[i].Pointer, Encoding.ASCII.GetString(Data.Skip(EventFileSections[i].Pointer).TakeWhile(b => b != 0).ToArray()));
            }

            for (int i = 0; i < EventFileSections.Count - 2; i++)
            {
                VoiceMapEntries.Add(new(Data.Skip(VoiceMapEntriesSectionOffset + i * VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH).Take(VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH), Log));
                VoiceMapEntries[^1].VoiceFileName = Encoding.ASCII.GetString(Data.Skip(VoiceMapEntries.Last().VoiceFileNamePointer).TakeWhile(b => b != 0).ToArray());
                VoiceMapEntries[^1].SetSubtitle(Encoding.GetEncoding("Shift-JIS").GetString(Data.Skip(VoiceMapEntries.Last().SubtitlePointer).TakeWhile(b => b != 0).ToArray())[4..], recenter: false);
            }

            InitializeDialogueAndEndPointers(decompressedData, offset, @override: true);
        }

        /// <summary>
        /// Gets ARM assembly source of this file
        /// </summary>
        /// <returns>A string containing an ARM assembly representation of this file</returns>
        public string GetSource()
        {
            StringBuilder sb = new();

            sb.AppendLine($".word {VoiceMapEntries.Count + 2}");
            sb.AppendLine(".word END_POINTERS");
            sb.AppendLine(".ascii \"SUBS\"");
            sb.AppendLine(".word ENTRY_SECTION");
            sb.AppendLine(".word 1");
            sb.AppendLine(".word DIALOGUE_SECTION");
            sb.AppendLine(".word 1");

            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine($".word FILENAME{i:D3}");
                sb.AppendLine(".word 1");
            }

            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine($"FILENAME{i:D3}: .string \"{VoiceMapEntries[i].VoiceFileName}\"");
                sb.AsmPadString(VoiceMapEntries[i].VoiceFileName, Encoding.ASCII);
            }

            sb.AppendLine("DIALOGUE_SECTION:");
            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                if (SpeakerCodeMap.TryGetValue(VoiceMapEntries[i].VoiceFileName.Split('_')[0], out Speaker speaker))
                {
                    sb.AppendLine($".word {(int)speaker}");
                }
                else
                {
                    sb.AppendLine($".word 1");
                }
                sb.AppendLine($"DRAMPERS{i:D3}: .word FILENAME{i:D3}");
                sb.AppendLine($"DIALOGUE{i:D3}: .word SUBTITLE{i:D3}");
            }
            sb.AppendLine(".skip 12");
            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine($"SUBTITLE{i:D3}: .string \"{VoiceMapEntries[i].GetRawSubtitle().EscapeShiftJIS()}\"");
                sb.AsmPadString(VoiceMapEntries[i].GetRawSubtitle(), Encoding.GetEncoding("Shift-JIS"));
            }
            sb.AppendLine(".skip 4");

            sb.AppendLine("ENTRY_SECTION:");
            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine(VoiceMapEntries[i].GetSource(i));
            }
            sb.AppendLine($".skip {VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH}");

            sb.AppendLine("END_POINTERS:");
            sb.AppendLine($".word {VoiceMapEntries.Count * 4}");
            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine($".word DRAMPERS{i:D3}");
                sb.AppendLine($".word DIALOGUE{i:D3}");
            }
            for (int i = 0; i < VoiceMapEntries.Count; i++)
            {
                sb.AppendLine($".word ENTRYFILE{i:D3}");
                sb.AppendLine($".word ENTRYSUBS{i:D3}");
            }

            return sb.ToString();
        }

        /// <inheritdoc/>
        public override void NewFile(string filename, ILogger log)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Data = new();
            string[] csvData = File.ReadAllLines(filename);
            Name = "VOICEMAPS";
            Log = log;

            List<int> filenamePointers = new();
            List<byte> filenameSection = new();
            List<string> filenames = new();
            List<int> dialogueLinePointers = new();
            List<byte> dialogueLinesSection = new();
            foreach (string line in csvData)
            {
                string[] fields = line.Split(',');

                filenamePointers.Add(filenameSection.Count);
                dialogueLinePointers.Add(dialogueLinesSection.Count);

                filenames.Add(fields[0]);
                filenameSection.AddRange(Encoding.ASCII.GetBytes(fields[0]));
                dialogueLinesSection.AddRange(Encoding.GetEncoding("Shift-JIS").GetBytes($"#P07{fields[1]}"));

                filenameSection.Add(0);
                dialogueLinesSection.Add(0);
                while (filenameSection.Count % 4 != 0)
                {
                    filenameSection.Add(0);
                }
                while (dialogueLinesSection.Count % 4 != 0)
                {
                    dialogueLinesSection.Add(0);
                }
            }

            int numFrontPointers = csvData.Length + 2;
            Data.AddRange(BitConverter.GetBytes(numFrontPointers));
            Data.AddRange(BitConverter.GetBytes(-1)); // Add placeholder for end pointers pointer
            Data.AddRange(Encoding.ASCII.GetBytes("SUBS")); // Add magic identifier
            Data.AddRange(BitConverter.GetBytes(-1)); // Add placeholder for entry section
            Data.AddRange(BitConverter.GetBytes(1));
            Data.AddRange(BitConverter.GetBytes(-1)); // Add placeholder for dialogue section
            Data.AddRange(BitConverter.GetBytes(1));

            int filenameSectionStart = Data.Count + filenamePointers.Count * 8;

            // add rest of front pointers (dramatis personae/filename pointers)
            Data.AddRange(filenamePointers.SelectMany(p =>
            {
                List<byte> s = new(BitConverter.GetBytes(p + filenameSectionStart));
                s.AddRange(new byte[] { 1, 0, 0, 0 });
                return s;
            }));
            // And then add them to dramatis personae
            for (int i = 0; i < filenamePointers.Count; i++)
            {
                DramatisPersonae.Add(filenamePointers[i] + filenameSectionStart, filenames[i]);
            }

            Data.AddRange(filenameSection);

            // Go back and insert the pointer to the dialogue section
            int dialogueSectionStart = Data.Count;
            Data.RemoveRange(0x14, 4);
            Data.InsertRange(0x14, BitConverter.GetBytes(dialogueSectionStart));

            // Account for the dialogue section leadin
            DialogueLinesPointer = Data.Count + dialogueLinePointers.Count * 12 + 12;

            for (int i = 0; i < dialogueLinePointers.Count; i++)
            {
                if (SpeakerCodeMap.TryGetValue(filenames[i].Split('_')[0], out Speaker speaker))
                {
                    Data.AddRange(BitConverter.GetBytes((int)speaker));
                }
                else
                {
                    Data.AddRange(BitConverter.GetBytes(1));
                }
                EndPointers.Add(Data.Count); // add this next pointer to the end pointers so it gets resolved
                EndPointerPointers.Add(filenamePointers[i] + filenameSectionStart);
                Data.AddRange(BitConverter.GetBytes(filenamePointers[i] + filenameSectionStart));
                EndPointers.Add(Data.Count); // add this next pointer to the end pointers so it gets resolved
                EndPointerPointers.Add(dialogueLinePointers[i] + DialogueLinesPointer);
                Data.AddRange(BitConverter.GetBytes(dialogueLinePointers[i] + DialogueLinesPointer));
            }
            Data.AddRange(new byte[12]);
            Data.AddRange(dialogueLinesSection);
            Data.AddRange(new byte[4]);

            // Initialize the dialogue lines
            for (int i = 0; i < dialogueLinePointers.Count; i++)
            {
                Speaker speaker = Speaker.HARUHI;
                if (SpeakerCodeMap.TryGetValue(filenames[i].Split('_')[0], out Speaker parsedSpeaker))
                {
                    speaker = parsedSpeaker;
                }
                DialogueLines.Add(new(speaker, filenames[i], filenamePointers[i] + filenameSectionStart, dialogueLinePointers[i] + DialogueLinesPointer, [.. Data]));
            }

            // Go back and insert the pointer to the entry section
            VoiceMapEntriesSectionOffset = Data.Count;
            Data.RemoveRange(0x0C, 4);
            Data.InsertRange(0x0C, BitConverter.GetBytes(VoiceMapEntriesSectionOffset));

            for (int i = 0; i < csvData.Length; i++)
            {
                string[] fields = csvData[i].Split(',');
                int lineLength = DialogueLines[i].Text.Sum(c => FontReplacementMap.ReverseLookup(c)?.Offset ?? 15);

                EndPointers.AddRange([Data.Count, Data.Count + 4]); // Add the next two pointers to end pointers
                EndPointerPointers.AddRange([filenamePointers[i] + filenameSectionStart, dialogueLinePointers[i] + DialogueLinesPointer]);
                AdxHeader header = null;
                if (!string.IsNullOrEmpty(VceDirPath))
                {
                    header = new(File.ReadAllBytes(Path.Combine(VceDirPath, $"{filenames[i]}.bin")), log);
                }
                VoiceMapEntry vmEntry = new()
                {
                    VoiceFileNamePointer = filenamePointers[i] + filenameSectionStart,
                    SubtitlePointer = dialogueLinePointers[i] + DialogueLinesPointer,
                    X = CenterSubtitle(lineLength),
                    YPos = Enum.Parse<VoiceMapEntry.YPosition>(fields[2]),
                    FontSize = 100,
                    TargetScreen = Enum.Parse<VoiceMapEntry.Screen>(fields[3]),
                    Timer = header is null ? int.Parse(fields[4]) : (int)((double)header.TotalSamples / header.SampleRate * 180 + 30), // 180 = 60fps * 3x/frame (the number of times the timer is decremented per frame); extra half-second for readability
                };

                VoiceMapEntries.Add(vmEntry);
                Data.AddRange(vmEntry.GetBytes());
            }
            Data.AddRange(new byte[VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH]);

            // Go back and insert the pointer to the end pointers section
            int endPointersSectionStart = Data.Count;
            Data.RemoveRange(0x04, 4);
            Data.InsertRange(0x04, BitConverter.GetBytes(endPointersSectionStart));

            Data.AddRange(BitConverter.GetBytes(EndPointers.Count));
            Data.AddRange(EndPointers.SelectMany(e => BitConverter.GetBytes(e)));
        }

        /// <inheritdoc/>
        public override void EditDialogueLine(int index, string newText)
        {
            newText = $"#P07{newText}";

            base.EditDialogueLine(index, newText);

            if (DialogueLines[index].Text.Contains('\n'))
            {
                Log.LogWarning($"File {Index} has subtitle too long ({index}) (starting with: {DialogueLines[index].Text[4..20]})");
            }

            string actualText = newText[4..];
            int lineLength = actualText.Sum(c => FontReplacementMap.ReverseLookup(c)?.Offset ?? 15);
            VoiceMapEntries[index].X = CenterSubtitle(lineLength);
            Data.RemoveRange(VoiceMapEntriesSectionOffset + VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH * index + 8, 2); // Replace X in Data
            Data.InsertRange(VoiceMapEntriesSectionOffset + VoiceMapEntry.VOICE_MAP_ENTRY_LENGTH * index + 8, BitConverter.GetBytes(VoiceMapEntries[index].X));
        }

        internal override void ShiftPointers(int shiftLocation, int shiftAmount)
        {
            base.ShiftPointers(shiftLocation, shiftAmount);

            if (VoiceMapEntriesSectionOffset > shiftLocation)
            {
                VoiceMapEntriesSectionOffset += shiftAmount;
            }
            if (DialogueLinesPointer > shiftLocation)
            {
                DialogueLinesPointer += shiftAmount;
            }

            foreach (VoiceMapEntry vmEntry in VoiceMapEntries)
            {
                if (vmEntry.VoiceFileNamePointer > shiftLocation)
                {
                    vmEntry.VoiceFileNamePointer += shiftAmount;
                }
                if (vmEntry.SubtitlePointer > shiftLocation)
                {
                    vmEntry.SubtitlePointer += shiftAmount;
                }
            }
        }

        private static short CenterSubtitle(int lineLength)
        {
            return (short)((256 - lineLength) / 2);
        }

        /// <summary>
        /// Map between voice file codes and speakers
        /// </summary>
        public static readonly Dictionary<string, Speaker> SpeakerCodeMap = new()
        {
            { "ANZ", Speaker.GIRL },
            { "HRH", Speaker.HARUHI },
            { "KYN", Speaker.KYON },
            { "KUN", Speaker.KUNIKIDA },
            { "KZM", Speaker.KOIZUMI },
            { "MKR", Speaker.MIKURU },
            { "NGT", Speaker.NAGATO },
            { "SIS", Speaker.KYON_SIS },
            { "TAN", Speaker.TANIGUCHI },
            { "TRY", Speaker.TSURUYA },
        };

        /// <summary>
        /// Individual 
        /// </summary>
        public class VoiceMapEntry
        {
            /// <summary>
            /// Length of a voice map entry in bytes
            /// </summary>
            public const int VOICE_MAP_ENTRY_LENGTH = 20;

            /// <summary>
            /// The screen that the voicemap struct should be placed on
            /// </summary>
            public enum Screen
            {
                /// <summary>
                /// The bottom screen (optimal)
                /// </summary>
                BOTTOM = 0,
                /// <summary>
                /// The top screen (risky)
                /// </summary>
                TOP = 1,
            }

            /// <summary>
            /// Y position of the subtitle on a particular screen
            /// </summary>
            public enum YPosition : short
            {
                /// <summary>
                /// Top of the screen
                /// </summary>
                TOP = 2,
                /// <summary>
                /// Just below the top of the screen
                /// </summary>
                BELOW_TOP = 40,
                /// <summary>
                /// Just above the bottom of the screen
                /// </summary>
                ABOVE_BOTTOM = 160,
                /// <summary>
                /// Bottom of the screen
                /// </summary>
                BOTTOM = 176,
            }

            private string _subtitle;
            private YPosition _yPosition;

            internal List<byte> Data { get; set; } = [];
            /// <summary>
            /// Pointer to voice filename
            /// </summary>
            public int VoiceFileNamePointer { get; set; }
            /// <summary>
            /// The voice filename
            /// </summary>
            public string VoiceFileName { get; set; }
            /// <summary>
            /// Pointer to subtitle
            /// </summary>
            public int SubtitlePointer { get; set; }
            /// <summary>
            /// The subtitle
            /// </summary>
            public string Subtitle { get => _subtitle[4..]; }
            /// <summary>
            /// The X position of the subtitle
            /// </summary>
            public short X { get; set; }
            /// <summary>
            /// The Y position of the subtitle
            /// </summary>
            public short Y { get; private set; }
            /// <summary>
            /// Easier way to map the Y-position
            /// </summary>
            public YPosition YPos 
            { 
                get => _yPosition; 
                set
                {
                    Y = (short)value;
                    _yPosition = value;
                }
            }
            /// <summary>
            /// The font size of the subtitle
            /// </summary>
            public short FontSize { get; set; }
            /// <summary>
            /// The target screen for the subtitle (top or bottom)
            /// </summary>
            public Screen TargetScreen { get; set; }
            /// <summary>
            /// The number of frames the subtitle should remain on screen for
            /// </summary>
            public int Timer { get; set; }

            /// <summary>
            /// Empty constructor
            /// </summary>
            public VoiceMapEntry()
            {
            }

            /// <summary>
            /// Create a voice map entry from VOICEMAP.S data
            /// </summary>
            /// <param name="data">VOICEMAP.S binary data</param>
            /// <param name="log">ILogger instance for error logging</param>
            public VoiceMapEntry(IEnumerable<byte> data, ILogger log)
            {
                if (data.Count() != VOICE_MAP_ENTRY_LENGTH)
                {
                    log.LogError($"Voice map struct data length must be 0x{VOICE_MAP_ENTRY_LENGTH:X2}, was 0x{data.Count():X2}");
                    return;
                }

                VoiceFileNamePointer = BitConverter.ToInt32(data.Take(4).ToArray());
                SubtitlePointer = BitConverter.ToInt32(data.Skip(4).Take(4).ToArray());
                X = BitConverter.ToInt16(data.Skip(8).Take(2).ToArray());
                Y = BitConverter.ToInt16(data.Skip(10).Take(2).ToArray());
                YPos = (YPosition)Y;
                FontSize = BitConverter.ToInt16(data.Skip(12).Take(2).ToArray());
                TargetScreen = (Screen)BitConverter.ToInt16(data.Skip(14).Take(2).ToArray());
                Timer = BitConverter.ToInt32(data.Skip(16).Take(4).ToArray());

                Data = data.ToList();
            }

            internal string GetRawSubtitle()
            {
                return _subtitle;
            }

            /// <summary>
            /// Sets and optionally recenters the subtitle
            /// </summary>
            /// <param name="value">The new text of the subtitle</param>
            /// <param name="fontReplacementMap">The font replacement map for character replacement</param>
            /// <param name="recenter">(Optional)If true, recenters the subtitle in the middle of the screen</param>
            public void SetSubtitle(string value, FontReplacementDictionary fontReplacementMap = null, bool recenter = true)
            {
                _subtitle = $"#P07{value}";
                if (recenter)
                {
                    X = CenterSubtitle(value.Sum(c => fontReplacementMap.ReverseLookup(c)?.Offset ?? 15));
                }
            }

            internal string GetSource(int currentVoiceFile)
            {
                StringBuilder sb = new();

                sb.AppendLine($"STRUCT{currentVoiceFile:D3}:");
                sb.AppendLine($"   STRUCTFILE{currentVoiceFile:D3}: .word FILENAME{currentVoiceFile:D3}");
                sb.AppendLine($"   STRUCTSUBS{currentVoiceFile:D3}: .word SUBTITLE{currentVoiceFile:D3}");
                sb.AppendLine($"   .short {X}");
                sb.AppendLine($"   .short {Y}");
                sb.AppendLine($"   .short {FontSize}");
                sb.AppendLine($"   .short {(short)TargetScreen}");
                sb.AppendLine($"   .word {Timer}");

                return sb.ToString();
            }

            internal byte[] GetBytes()
            {
                Data.Clear();

                Data.AddRange(BitConverter.GetBytes(VoiceFileNamePointer));
                Data.AddRange(BitConverter.GetBytes(SubtitlePointer));
                Data.AddRange(BitConverter.GetBytes(X));
                Data.AddRange(BitConverter.GetBytes(Y));
                Data.AddRange(BitConverter.GetBytes(FontSize));
                Data.AddRange(BitConverter.GetBytes((short)TargetScreen));
                Data.AddRange(BitConverter.GetBytes(Timer));

                return [.. Data];
            }
        }
    }
}
