﻿using HaruhiChokuretsuLib.Archive;
using HaruhiChokuretsuLib.Archive.Data;
using HaruhiChokuretsuLib.Archive.Event;
using HaruhiChokuretsuLib.Archive.Graphics;
using HaruhiChokuretsuLib.Util;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaruhiChokuretsuTests;

public class SourceTests
{
    private ConsoleLogger _log = new();

    private static readonly string[] _mapFileNames =
    [
        "BUND0S",
        "BUNN0S",
        "BUNN1S",
        "COMD0S",
        "K15D0S",
        "ONGD0S",
        "ONGN0S",
        "TAID0S",
        "POOD0S",
        "KYND0S",
        "SLTD0S",
        "SLTD1S",
        "SL0D0S",
        "SL1D0S",
        "SL2D0S",
        "SL3D0S",
        "SL4D0S",
        "SL5D0S",
        "SL6D0S",
        "SL7D0S",
        "POON0S",
        "ROKD0S",
        "ROUD0S",
        "AKIN0S",
        "SL1D1S",
        "SL2D1S",
        "SL3D1S",
        "SL4D1S",
        "SL5D1S",
        "AKID0S",
        "XTRD0S",
        "SL8D0S",
    ];

    private static readonly string[] _puzzleFileNames =
    [
        "SLG01S",
        "SLG10S",
        "SLG11S",
        "SLG20S",
        "SLG30S",
        "SLG40S",
        "SLG50S",
        "SLG60S",
        "SLG70S",
        "SLG80S",
    ];

    private static int[] GetEvtFileIndices()
    {
        List<int> indices = [];
        for (int i = 1; i <= 588; i++)
        {
            if (!new int[] { 106, 537, 580, 581, 588 }.Contains(i))
            {
                indices.Add(i);
            }
        }
        return indices.ToArray();
    }

    private static string[] GetChessFileIndices()
    {
        List<string> names = ["CHS00S"];
        for (int i = 1; i <= 100; i++)
        {
            names.Add($"CHS{i:D3}S");
        }
        return names.ToArray();
    }

    private static async Task<byte[]> CompileFromSource(string source)
    {
        string filePath = @$"./file-{Guid.NewGuid()}.s"; // Guid for uniqueness so we can run these tests in parallel
        string devkitArm = OperatingSystem.IsWindows() ? @"C:\devkitPro\devkitARM" : "/opt/devkitpro/devkitARM";
        File.WriteAllText(filePath, source);

        string objFile = $"{Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath))}.o";
        string binFile = $"{Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath))}.bin";

        string exe = OperatingSystem.IsWindows() ? ".exe" : "";
        ProcessStartInfo gcc = new(Path.Combine(devkitArm, $"bin/arm-none-eabi-gcc{exe}"), $"-c -nostdlib -static \"{filePath}\" -o \"{objFile}");
        await Process.Start(gcc).WaitForExitAsync();
        await Task.Delay(50); // ensures process is actually complete
        ProcessStartInfo objcopy = new(Path.Combine(devkitArm, $"bin/arm-none-eabi-objcopy{exe}"), $"-O binary \"{objFile}\" \"{binFile}");
        await Process.Start(objcopy).WaitForExitAsync();
        await Task.Delay(50); // ensures process is actually copmlete
        byte[] bytes = File.ReadAllBytes(binFile);
        File.Delete(filePath);
        File.Delete(objFile);
        File.Delete(binFile);

        return bytes;
    }

    [OneTimeSetUp]
    public static void Setup()
    {
        ConsoleLogger log = new();
        if (!File.Exists("COMMANDS.INC"))
        {
            StringBuilder sb = new();
            foreach (ScriptCommand command in EventFile.CommandsAvailable)
            {
                sb.AppendLine(command.GetMacro());
            }
            File.WriteAllText("COMMANDS.INC", sb.ToString());
        }
        if (!File.Exists("DATBIN.INC"))
        {
            var grp = ArchiveFile<GraphicsFile>.FromFile(@"./inputs/dat.bin", log);
            File.WriteAllText("DATBIN.INC", grp.GetSourceInclude());
        }
        if (!File.Exists("EVTBIN.INC"))
        {
            var grp = ArchiveFile<GraphicsFile>.FromFile(@"./inputs/evt.bin", log);
            File.WriteAllText("EVTBIN.INC", grp.GetSourceInclude());
        }
        if (!File.Exists("GRPBIN.INC"))
        {
            var grp = ArchiveFile<GraphicsFile>.FromFile(@"./inputs/grp.bin", log);
            File.WriteAllText("GRPBIN.INC", grp.GetSourceInclude());
        }
    }

    [OneTimeTearDown]
    public static void TearDown()
    {
        if (File.Exists("COMMANDS.INC"))
        {
            File.Delete("COMMANDS.INC");
        }
        if (File.Exists("DATBIN.INC"))
        {
            File.Delete("DATBIN.INC");
        }
        if (File.Exists("EVTBIN.INC"))
        {
            File.Delete("EVTBIN.INC");
        }
        if (File.Exists("GRPBIN.INC"))
        {
            File.Delete("GRPBIN.INC");
        }
    }

    [Test]
    [TestCaseSource(nameof(_mapFileNames))]
    [Parallelizable(ParallelScope.All)]
    public async Task MapSourceTest(string mapFileName)
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        MapFile mapFile = dat.GetFileByName(mapFileName).CastTo<MapFile>();

        byte[] newBytes = await CompileFromSource(mapFile.GetSource(new()));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(mapFile.Data, newBytesList);
    }

    [Test]
    [TestCaseSource(nameof(_puzzleFileNames))]
    [Parallelizable(ParallelScope.All)]
    public async Task PuzzleSourceTest(string puzzleFileName)
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        PuzzleFile puzzleFile = dat.GetFileByName(puzzleFileName).CastTo<PuzzleFile>();

        byte[] newBytes = await CompileFromSource(puzzleFile.GetSource(new() { { "GRPBIN", File.ReadAllLines("GRPBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(puzzleFile.Data, newBytesList);
    }

    [Test]
    [TestCaseSource(nameof(GetEvtFileIndices))]
    [Parallelizable(ParallelScope.All)]
    public async Task EvtSourceTest(int evtFileIndex)
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<EventFile> evt = ArchiveFile<EventFile>.FromFile(@"./inputs/evt.bin", _log);
        EventFile eventFile = evt.GetFileByIndex(evtFileIndex);

        byte[] newBytes = await CompileFromSource(eventFile.GetSource([]));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(eventFile.Data, newBytesList);
    }

    [Test]
    public async Task ChessSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<EventFile> evt = ArchiveFile<EventFile>.FromFile(@"./inputs/evt.bin", _log);
        EventFile chessFile = evt.GetFileByName("CHESSS");
            
        byte[] newBytes = await CompileFromSource(chessFile.GetSource([]));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(chessFile.Data, newBytesList);
    }

    [Test]
    public async Task QmapSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        QMapFile qmapFile = dat.GetFileByName("QMAPS").CastTo<QMapFile>();

        byte[] newBytes = await CompileFromSource(qmapFile.GetSource(new()));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(qmapFile.Data, newBytesList);
    }

    [Test]
    public async Task MessInfoSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        MessageInfoFile messageInfoFile = dat.GetFileByName("MESSINFOS").CastTo<MessageInfoFile>();

        byte[] newBytes = await CompileFromSource(messageInfoFile.GetSource(new()));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(messageInfoFile.Data, newBytesList);
    }

    [Test]
    public async Task PlaceSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        PlaceFile placeFile = dat.GetFileByName("PLACES").CastTo<PlaceFile>();
            
        byte[] newBytes = await CompileFromSource(placeFile.GetSource(new() { { "GRPBIN", File.ReadAllLines("GRPBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(placeFile.Data, newBytesList);
    }

    [Test]
    public async Task ChibiSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        ChibiFile chibiFile = dat.GetFileByName("CHIBIS").CastTo<ChibiFile>();

        byte[] newBytes = await CompileFromSource(chibiFile.GetSource(new() { { "GRPBIN", File.ReadAllLines("GRPBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(chibiFile.Data, newBytesList);
    }

    [Test]
    public async Task ChrDataSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        CharacterDataFile characterDataFile = dat.GetFileByName("CHRDATAS").CastTo<CharacterDataFile>();

        byte[] newBytes = await CompileFromSource(characterDataFile.GetSource(new() { { "GRPBIN", File.ReadAllLines("GRPBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(characterDataFile.Data, newBytesList);
    }

    [Test]
    public async Task EventTableSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<EventFile> evt = ArchiveFile<EventFile>.FromFile(@"./inputs/evt.bin", _log);
        EventFile evtTblFile = evt.GetFileByName("EVTTBLS");
        evtTblFile.InitializeEventTableFile();

        byte[] newBytes = await CompileFromSource(evtTblFile.GetSource(new() { { "EVTBIN", File.ReadAllLines("EVTBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        Assert.That(evtTblFile.Data, Is.EqualTo(newBytesList));
    }

    [Test]
    public async Task ExtraSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        ExtraFile extraFile = dat.GetFileByName("EXTRAS").CastTo<ExtraFile>();

        byte[] newBytes = await CompileFromSource(extraFile.GetSource(new()));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(extraFile.Data, newBytesList);
    }

    [Test]
    public async Task ItemSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        ItemFile itemFile = dat.GetFileByName("ITEMS").CastTo<ItemFile>();

        byte[] newBytes = await CompileFromSource(itemFile.GetSource(new() { { "GRPBIN", File.ReadAllLines("GRPBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(itemFile.Data, newBytesList);
    }

    [Test]
    public async Task ScenarioSourceTest()
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<EventFile> evt = ArchiveFile<EventFile>.FromFile(@"./inputs/evt.bin", _log);
        EventFile scenarioFile = evt.GetFileByName("SCENARIOS").CastTo<EventFile>();

        byte[] newBytes = await CompileFromSource(scenarioFile.GetSource(new() { { "DATBIN", File.ReadAllLines("DATBIN.INC").Select(i => new IncludeEntry(i)).ToArray() }, { "EVTBIN", File.ReadAllLines("EVTBIN.INC").Select(i => new IncludeEntry(i)).ToArray() } }));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(scenarioFile.Data, newBytesList);
    }

    [Test]
    [TestCaseSource(nameof(GetChessFileIndices))]
    [Parallelizable(ParallelScope.All)]
    public async Task ChessSourceTest(string chessFileName)
    {
        // This file can be ripped directly from the ROM
        ArchiveFile<DataFile> dat = ArchiveFile<DataFile>.FromFile(@"./inputs/dat.bin", _log);
        ChessFile chessFile = dat.GetFileByName(chessFileName).CastTo<ChessFile>();
            
        byte[] newBytes = await CompileFromSource(chessFile.GetSource([]));
        List<byte> newBytesList = new(newBytes);
        if (newBytes.Length % 16 > 0)
        {
            newBytesList.AddRange(new byte[16 - (newBytes.Length % 16)]);
        }

        ClassicAssert.AreEqual(chessFile.Data, newBytesList);
    }
}