﻿/*
 * MassEffectModder
 *
 * Copyright (C) 2016-2018 Pawel Kolodziejski <aquadran at users.sourceforge.net>
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.

 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.

 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301, USA.
 *
 */

using Microsoft.Win32;
using StreamHelpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace MassEffectModder
{
    static public class CmdLineTools
    {
        public const uint textureMapBinTag = 0x5054454D;
        public const uint textureMapBinVersion = 2;
        public const uint TextureModTag = 0x444F4D54;
        public const uint FileTextureTag = 0x53444446;
        public const uint FileBinTag = 0x4E494246;
        public const uint TextureModVersion = 2;
        const uint MEMI_TAG = 0x494D454D;

        public struct BinaryMod
        {
            public string packagePath;
            public int exportId;
            public byte[] data;
            public int binaryModType;
            public string textureName;
            public uint textureCrc;
            public long offset;
            public long size;
        };

        static List<FoundTexture> textures;
        static TreeScan treeScan;
        static public List<string> pkgsToRepack = null;

        static public bool applyModTag(int gameId, int MeuitmV, int AlotV)
        {
            string path = "";
            if (gameId == (int)MeType.ME1_TYPE)
            {
                path = GameData.GamePath + @"\BioGame\CookedPC\testVolumeLight_VFX.upk";
            }
            if (gameId == (int)MeType.ME2_TYPE)
            {
                path = GameData.GamePath + @"\BioGame\CookedPC\BIOC_Materials.pcc";
            }
            if (gameId == (int)MeType.ME3_TYPE)
            {
                path = GameData.GamePath + @"\BIOGame\CookedPCConsole\adv_combat_tutorial_xbox_D_Int.afc";
            }
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.Seek(-16, SeekOrigin.End);
                    int prevMeuitmV = fs.ReadInt32();
                    int prevAlotV = fs.ReadInt32();
                    int prevProductV = fs.ReadInt32();
                    uint memiTag = fs.ReadUInt32();
                    if (memiTag == MEMI_TAG)
                    {
                        if (prevProductV < 10 || prevProductV == 4352 || prevProductV == 16777472) // default before MEM v178
                            prevProductV = prevAlotV = prevMeuitmV = 0;
                    }
                    else
                        prevProductV = prevAlotV = prevMeuitmV = 0;
                    if (MeuitmV != 0)
                        prevMeuitmV = MeuitmV;
                    if (AlotV != 0)
                        prevAlotV = AlotV;
                    fs.WriteInt32(prevMeuitmV);
                    fs.WriteInt32(prevAlotV);
                    fs.WriteInt32((int)(prevProductV & 0xffff0000) | int.Parse(Application.ProductVersion));
                    fs.WriteUInt32(MEMI_TAG);
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        static private void loadTexturesMap(MeType gameId)
        {
            Stream fs;
            textures = new List<FoundTexture>();
            byte[] buffer = null;
            List<string> pkgs;
            if (gameId == MeType.ME1_TYPE)
                pkgs = Program.tablePkgsME1;
            else if (gameId == MeType.ME2_TYPE)
                pkgs = Program.tablePkgsME2;
            else
                pkgs = Program.tablePkgsME3;

            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resources = assembly.GetManifestResourceNames();
            for (int l = 0; l < resources.Length; l++)
            {
                if (resources[l].Contains("me" + (int)gameId + "map.bin"))
                {
                    using (Stream s = Assembly.GetEntryAssembly().GetManifestResourceStream(resources[l]))
                    {
                        buffer = s.ReadToBuffer(s.Length);
                        break;
                    }
                }
            }
            if (buffer == null)
                throw new Exception();
            MemoryStream tmp = new MemoryStream(buffer);
            if (tmp.ReadUInt32() != 0x504D5443)
                throw new Exception();
            byte[] decompressed = new byte[tmp.ReadUInt32()];
            byte[] compressed = tmp.ReadToBuffer(tmp.ReadUInt32());
            if (new ZlibHelper.Zlib().Decompress(compressed, (uint)compressed.Length, decompressed) == 0)
                throw new Exception();
            fs = new MemoryStream(decompressed);

            fs.Skip(8);
            uint countTexture = fs.ReadUInt32();
            for (int i = 0; i < countTexture; i++)
            {
                FoundTexture texture = new FoundTexture();
                int len = fs.ReadByte();
                texture.name = fs.ReadStringASCII(len);
                texture.crc = fs.ReadUInt32();
                texture.width = fs.ReadInt16();
                texture.height = fs.ReadInt16();
                texture.pixfmt = (PixelFormat)fs.ReadByte();
                texture.alphadxt1 = fs.ReadByte() != 0;
                int countPackages = fs.ReadInt16();
                texture.list = new List<MatchedTexture>();
                for (int k = 0; k < countPackages; k++)
                {
                    MatchedTexture matched = new MatchedTexture();
                    matched.exportID = fs.ReadInt32();
                    if (gameId == MeType.ME1_TYPE)
                    {
                        matched.linkToMaster = fs.ReadInt16();
                        if (matched.linkToMaster != -1)
                        {
                            matched.slave = true;
                            matched.basePackageName = fs.ReadStringASCIINull();
                        }
                    }
                    matched.removeEmptyMips = fs.ReadByte() != 0;
                    matched.numMips = fs.ReadByte();
                    matched.path = pkgs[fs.ReadInt16()];
                    matched.packageName = Path.GetFileNameWithoutExtension(matched.path).ToUpper();
                    texture.list.Add(matched);
                }
                textures.Add(texture);
            }
        }

        static public bool loadTexturesMapFile(string path, bool ipc)
        {
            textures = new List<FoundTexture>();

            if (!File.Exists(path))
            {
                if (ipc)
                {
                    Console.WriteLine("[IPC]ERROR_TEXTURE_MAP_MISSING");
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("Missing textures scan file!" + Environment.NewLine);
                }
                return false;
            }

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                uint tag = fs.ReadUInt32();
                uint version = fs.ReadUInt32();
                if (tag != textureMapBinTag || version != textureMapBinVersion)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR_TEXTURE_MAP_WRONG");
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("Detected wrong or old version of textures scan file!" + Environment.NewLine);
                    }
                    return false;
                }

                uint countTexture = fs.ReadUInt32();
                for (int i = 0; i < countTexture; i++)
                {
                    FoundTexture texture = new FoundTexture();
                    int len = fs.ReadInt32();
                    texture.name = fs.ReadStringASCII(len);
                    texture.crc = fs.ReadUInt32();
                    uint countPackages = fs.ReadUInt32();
                    texture.list = new List<MatchedTexture>();
                    for (int k = 0; k < countPackages; k++)
                    {
                        MatchedTexture matched = new MatchedTexture();
                        matched.exportID = fs.ReadInt32();
                        matched.linkToMaster = fs.ReadInt32();
                        len = fs.ReadInt32();
                        matched.path = fs.ReadStringASCII(len);
                        texture.list.Add(matched);
                    }
                    textures.Add(texture);
                }

                List<string> packages = new List<string>();
                int numPackages = fs.ReadInt32();
                for (int i = 0; i < numPackages; i++)
                {
                    int len = fs.ReadInt32();
                    string pkgPath = fs.ReadStringASCII(len);
                    pkgPath = GameData.GamePath + pkgPath;
                    packages.Add(pkgPath);
                }
                for (int i = 0; i < packages.Count; i++)
                {
                    if (GameData.packageFiles.Find(s => s.Equals(packages[i], StringComparison.OrdinalIgnoreCase)) == null)
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_REMOVED_FILE Detected removal of game files since last game data scan.");
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Detected removal of game files since last game data scan." + Environment.NewLine + Environment.NewLine);
                        }
                        return false;
                    }
                }
                for (int i = 0; i < GameData.packageFiles.Count; i++)
                {
                    if (packages.Find(s => s.Equals(GameData.packageFiles[i], StringComparison.OrdinalIgnoreCase)) == null)
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_ADDED_FILE Detected additional game files not present in latest game data scan.");
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Detected additional game files not present in latest game data scan." + Environment.NewLine + Environment.NewLine);
                        }
                        return false;
                    }
                }
            }

            return true;
        }

        static public bool checkWriteAccessDir(string path)
        {
            try
            {
                using (FileStream fs = File.Create(Path.Combine(path, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static public bool checkWriteAccessFile(string path)
        {
            try
            {
                using (FileStream fs = File.OpenWrite(path)) { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        static public bool isRunAsAdministrator()
        {
            return (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
        }

        static public bool CheckAndCorrectAccessToGame(MeType gameId)
        {
            bool writeAccess = false;
            if (checkWriteAccessDir(GameData.MainData))
                writeAccess = true;

            bool uac = false;
            int? value = (int?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", null);
            if (value != null && value > 0)
                uac = true;

            bool registryExists = false;
            if (gameId == MeType.ME1_TYPE)
            {
                string keyRegistry = @"SOFTWARE\WOW6432Node\AGEIA Technologies";
                RegistryKey key;
                if (isRunAsAdministrator())
                {
                    string userName = WindowsIdentity.GetCurrent().Name;
                    key = Registry.LocalMachine.CreateSubKey(keyRegistry);
                    RegistrySecurity security = new RegistrySecurity();
                    security = key.GetAccessControl();
                    security.AddAccessRule(new RegistryAccessRule(userName, RegistryRights.WriteKey |
                        RegistryRights.ReadKey | RegistryRights.Delete |
                        RegistryRights.FullControl, InheritanceFlags.ContainerInherit |
                        InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                    key.SetAccessControl(security);
                    key.Close();
                    registryExists = true;
                }
                else
                {
                    try
                    {
                        key = Registry.LocalMachine.OpenSubKey(keyRegistry, true);
                    }
                    catch
                    {
                        key = null;
                    }

                    if (key != null)
                    {
                        key.Close();
                        registryExists = true;
                    }
                }
            }

            string msg;
            if (!uac && ((gameId == MeType.ME1_TYPE && !registryExists) || !writeAccess))
            {
                msg = "MEM is not able to";
                if (!writeAccess)
                {
                    msg += " grant write access to game folders";
                }
                if (gameId == MeType.ME1_TYPE && !registryExists)
                {
                    if (!writeAccess)
                        msg += " and";
                    msg += " fix a ME1 launch issue";
                }
                msg += " because MEM does not have administrative rights and UAC is disabled.";
                Console.WriteLine(msg);
                return false;
            }

            if (!writeAccess || (gameId == MeType.ME1_TYPE && !registryExists))
            {
                msg = "Some";
                if (!writeAccess)
                {
                    msg += " game folders";
                }
                if (gameId == MeType.ME1_TYPE && !registryExists)
                {
                    if (!writeAccess)
                        msg += " and";
                    msg += " registry keys";
                }

                msg += " are not writeable by your user account.\nMEM will attempt to grant access to";

                if (!writeAccess)
                {
                    msg += " game folders";
                }
                if (gameId == MeType.ME1_TYPE && !registryExists)
                {
                    if (!writeAccess)
                        msg += " and";
                    msg += " registry keys";
                }

                msg += " with PermissionsGranter.exe program.\n\n";

                if (!writeAccess)
                {
                    msg += "Game folder: " + GameData.GamePath + "\n\n";
                }
                if (gameId == MeType.ME1_TYPE && !registryExists)
                {
                    msg += "Registry: HKLM\\SOFTWARE\\WOW6432Node\\AGEIA Technologies\n";
                    msg += "(Fixes a ME1 launch issue)";
                }

                Console.WriteLine(msg, "Granting permissions");

                bool failedAccess = true;
                string userName = WindowsIdentity.GetCurrent().Name;
                try
                {
                    Process process = new Process();
                    process.StartInfo.FileName = Path.Combine(Program.dllPath, "PermissionsGranter.exe");
                    process.StartInfo.Arguments = "\"" + userName + "\"";
                    if (gameId == MeType.ME1_TYPE && !registryExists)
                        process.StartInfo.Arguments += " -create-hklm-reg-key \"SOFTWARE\\WOW6432Node\\AGEIA Technologies\"";
                    if (!writeAccess)
                        process.StartInfo.Arguments += " \"" + GameData.GamePath + "\"";
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.Verb = "runas";
                    process.Start();
                    process.WaitForExit(60000);
                    if (process.ExitCode == 0)
                        failedAccess = false;
                }
                catch
                {
                    failedAccess = true;
                }

                if (failedAccess)
                {
                    msg = "MEM is not able to";
                    if (!writeAccess)
                    {
                        msg += " grant write access to game folders";
                    }
                    if (gameId == MeType.ME1_TYPE && !registryExists)
                    {
                        if (!writeAccess)
                            msg += " and";
                        msg += " fix a ME1 launch issue";
                    }
                    msg += " because MEM does not have administrative rights.";
                    Console.WriteLine(msg);
                    return false;
                }

                registryExists = true;
            }

            if (gameId == MeType.ME1_TYPE)
                Misc.ApplyLAAForME1Exe();

            if (gameId == MeType.ME1_TYPE && registryExists)
            {
                string gameExePath = GameData.GameExePath;
                RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", true);
                if (key != null)
                {
                    string entry = (string)key.GetValue(gameExePath, null);
                    if (entry != null)
                    {
                        entry = entry.Replace("RUNASADMIN", "");
                        entry = entry.Replace("WINXPSP3", "");
                        key.SetValue(gameExePath, entry);
                    }
                    key.Close();
                }

                Misc.ChangeProductNameForME1Exe();
            }

            return true;
        }

        static public bool convertDataModtoMem(string inputDir, string memFilePath, MeType gameId, bool ipc = false)
        {
            string[] files = null;

            loadTexturesMap(gameId);

            Console.WriteLine("Mods conversion started...");

            List<string> list;
            List<string> list2;
            list = Directory.GetFiles(inputDir, "*.mem", SearchOption.AllDirectories).Where(item => item.EndsWith(".mem", StringComparison.OrdinalIgnoreCase)).ToList();
            list.Sort();
            list2 = Directory.GetFiles(inputDir, "*.tpf", SearchOption.AllDirectories).Where(item => item.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).ToList();
            list2.AddRange(Directory.GetFiles(inputDir, "*.mod", SearchOption.AllDirectories).Where(item => item.EndsWith(".mod", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.bin", SearchOption.AllDirectories).Where(item => item.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.xdelta", SearchOption.AllDirectories).Where(item => item.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.dds", SearchOption.AllDirectories).Where(item => item.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.png", SearchOption.AllDirectories).Where(item => item.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.bmp", SearchOption.AllDirectories).Where(item => item.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.tga", SearchOption.AllDirectories).Where(item => item.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.jpg", SearchOption.AllDirectories).Where(item => item.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));
            list2.AddRange(Directory.GetFiles(inputDir, "*.jpeg", SearchOption.AllDirectories).Where(item => item.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)));
            list2.Sort();
            list.AddRange(list2);
            files = list.ToArray();

            int result;
            string fileName = "";
            ulong dstLen = 0;
            string[] ddsList = null;
            ulong numEntries = 0;
            FileStream outFs;

            List<BinaryMod> mods = new List<BinaryMod>();
            List<MipMaps.FileMod> modFiles = new List<MipMaps.FileMod>();

            if (File.Exists(memFilePath))
                File.Delete(memFilePath);
            outFs = new FileStream(memFilePath, FileMode.Create, FileAccess.Write);
            outFs.WriteUInt32(TextureModTag);
            outFs.WriteUInt32(TextureModVersion);
            outFs.WriteInt64(0); // filled later

            int lastProgress = -1;
            for (int n = 0; n < files.Count(); n++)
            {
                string file = files[n];
                string relativeFilePath = file.Substring(inputDir.TrimEnd('\\').Length + 1);
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + Path.GetFileName(file));
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("File: " + relativeFilePath);
                }

                int newProgress = (n * 100) / files.Count();
                if (ipc && lastProgress != newProgress)
                {
                    Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                    Console.Out.Flush();
                    lastProgress = newProgress;
                }

                if (file.EndsWith(".mem", StringComparison.OrdinalIgnoreCase))
                {
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        uint tag = fs.ReadUInt32();
                        uint version = fs.ReadUInt32();
                        if (tag != TextureModTag || version != TextureModVersion)
                        {
                            if (version != TextureModVersion)
                            {
                                Console.WriteLine("File " + relativeFilePath + " was made with an older version of MEM, skipping...");
                            }
                            else
                            {
                                Console.WriteLine("File " + relativeFilePath + " is not a valid MEM mod, skipping...");
                            }
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                Console.Out.Flush();
                            }
                            continue;
                        }
                        else
                        {
                            uint gameType = 0;
                            fs.JumpTo(fs.ReadInt64());
                            gameType = fs.ReadUInt32();
                            if ((MeType)gameType != gameId)
                            {
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                    Console.Out.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("File " + relativeFilePath + " is not a MEM mod valid for this game");
                                }
                                continue;
                            }
                        }
                        int numFiles = fs.ReadInt32();
                        for (int l = 0; l < numFiles; l++)
                        {
                            MipMaps.FileMod fileMod = new MipMaps.FileMod();
                            fileMod.tag = fs.ReadUInt32();
                            fileMod.name = fs.ReadStringASCIINull();
                            fileMod.offset = fs.ReadInt64();
                            fileMod.size = fs.ReadInt64();
                            long prevPos = fs.Position;
                            fs.JumpTo(fileMod.offset);
                            fileMod.offset = outFs.Position;
                            if (fileMod.tag == MipMaps.FileTextureTag)
                            {
                                outFs.WriteStringASCIINull(fs.ReadStringASCIINull());
                                outFs.WriteUInt32(fs.ReadUInt32());
                            }
                            else if (fileMod.tag == MipMaps.FileBinaryTag)
                            {
                                outFs.WriteInt32(fs.ReadInt32());
                                outFs.WriteStringASCIINull(fs.ReadStringASCIINull());
                            }
                            outFs.WriteFromStream(fs, fileMod.size);
                            fs.JumpTo(prevPos);
                            modFiles.Add(fileMod);
                        }
                    }
                }
                else if (file.EndsWith(".mod", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                        {
                            string package = "";
                            int len = fs.ReadInt32();
                            string version = fs.ReadStringASCIINull();
                            if (version.Length < 5) // legacy .mod
                                fs.SeekBegin();
                            else
                            {
                                fs.SeekBegin();
                                len = fs.ReadInt32();
                                version = fs.ReadStringASCII(len); // version
                            }
                            numEntries = fs.ReadUInt32();
                            for (uint i = 0; i < numEntries; i++)
                            {
                                BinaryMod mod = new BinaryMod();
                                len = fs.ReadInt32();
                                string desc = fs.ReadStringASCII(len); // description
                                len = fs.ReadInt32();
                                string scriptLegacy = fs.ReadStringASCII(len);
                                string path = "";
                                if (desc.Contains("Binary Replacement"))
                                {
                                    try
                                    {
                                        Misc.ParseME3xBinaryScriptMod(scriptLegacy, ref package, ref mod.exportId, ref path);
                                        if (mod.exportId == -1 || package == "" || path == "")
                                            throw new Exception();
                                    }
                                    catch
                                    {
                                        len = fs.ReadInt32();
                                        fs.Skip(len);
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Skipping not compatible content, entry: " + (i + 1) + " - mod: " + relativeFilePath);
                                        }
                                        continue;
                                    }
                                    mod.packagePath = Path.Combine(path, package);
                                    mod.binaryModType = 1;
                                    len = fs.ReadInt32();
                                    mod.data = fs.ReadToBuffer(len);
                                }
                                else
                                {
                                    string textureName = desc.Split(' ').Last();
                                    FoundTexture f;
                                    try
                                    {
                                        f = Misc.ParseLegacyMe3xScriptMod(textures, scriptLegacy, textureName);
                                        mod.textureCrc = f.crc;
                                        if (mod.textureCrc == 0)
                                            throw new Exception();
                                    }
                                    catch
                                    {
                                        len = fs.ReadInt32();
                                        fs.Skip(len);
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Skipping not compatible content, entry: " + (i + 1) + " - mod: " + relativeFilePath);
                                        }
                                        continue;
                                    }
                                    textureName = f.name;
                                    mod.textureName = textureName;
                                    mod.binaryModType = 0;
                                    len = fs.ReadInt32();
                                    mod.data = fs.ReadToBuffer(len);

                                    PixelFormat pixelFormat = f.pixfmt;
                                    Image image = new Image(mod.data, Image.ImageFormat.DDS);

                                    if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                                        f.width / f.height)
                                    {
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Error in texture: " + textureName + string.Format("_0x{0:X8}", f.crc) +
                                                " This texture has wrong aspect ratio, skipping texture, entry: " + (i + 1) +
                                                " - mod: " + relativeFilePath);
                                        }
                                        continue;
                                    }

                                    if (!image.checkDDSHaveAllMipmaps() ||
                                       (f.list.Find(s => s.path != "").numMips > 1 && image.mipMaps.Count() <= 1) ||
                                        image.pixelFormat != pixelFormat)
                                    {
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]PROCESSING_FILE Converting " + textureName);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Converting/correcting texture: " + textureName);
                                        }
                                        bool dxt1HasAlpha = false;
                                        byte dxt1Threshold = 128;
                                        if (f.alphadxt1)
                                        {
                                            dxt1HasAlpha = true;
                                            if (image.pixelFormat == PixelFormat.ARGB ||
                                                image.pixelFormat == PixelFormat.DXT3 ||
                                                image.pixelFormat == PixelFormat.DXT5)
                                            {
                                                Console.WriteLine("Warning for texture: " + textureName + ". This texture converted from full alpha to binary alpha.");
                                            }
                                        }
                                        image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                                        mod.data = image.StoreImageToDDS();
                                    }
                                }
                                mods.Add(mod);
                            }
                        }
                    }
                    catch
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Mod is not compatible: " + relativeFilePath);
                        }
                        continue;
                    }
                }
                else if (file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                         file.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
                {
                    BinaryMod mod = new BinaryMod();
                    try
                    {
                        string filename = Path.GetFileNameWithoutExtension(file);
                        string dlcName = "";
                        int posStr = 0;
                        if (filename.ToUpperInvariant()[0] == 'D')
                        {
                            string tmpDLC = filename.Split('-')[0];
                            int lenDLC = int.Parse(tmpDLC.Substring(1));
                            dlcName = filename.Substring(tmpDLC.Length + 1, lenDLC);
                            posStr += tmpDLC.Length + lenDLC + 1;
                            if (filename[posStr++] != '-')
                                throw new Exception();
                        }
                        else if (filename.ToUpperInvariant()[0] == 'B')
                        {
                            posStr += 1;
                        }
                        else
                            throw new Exception();
                        string tmpPkg = filename.Substring(posStr).Split('-')[0];
                        posStr += tmpPkg.Length + 1;
                        int lenPkg = int.Parse(tmpPkg.Substring(0));
                        string pkgName = filename.Substring(posStr, lenPkg);
                        posStr += lenPkg;
                        if (filename[posStr++] != '-')
                            throw new Exception();
                        if (filename.ToUpperInvariant()[posStr++] != 'E')
                            throw new Exception();
                        string tmpExp = filename.Substring(posStr);
                        mod.exportId = int.Parse(tmpExp.Substring(0));
                        if (dlcName != "")
                        {
                            if (gameId == MeType.ME1_TYPE)
                                mod.packagePath = @"\DLC\" + dlcName + @"\CookedPC\" + pkgName;
                            else if (gameId == MeType.ME2_TYPE)
                                mod.packagePath = @"\BioGame\DLC\" + dlcName + @"\CookedPC\" + pkgName;
                            else
                                mod.packagePath = @"\BIOGame\DLC\" + dlcName + @"\CookedPCConsole\" + pkgName;
                        }
                        else
                        {
                            if (gameId == MeType.ME1_TYPE || gameId == MeType.ME2_TYPE)
                                mod.packagePath = @"\BioGame\CookedPC\" + pkgName;
                            else
                                mod.packagePath = @"\BIOGame\CookedPCConsole\" + pkgName;
                        }
                        if (file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                            mod.binaryModType = 1;
                        else
                         if (file.EndsWith(".xdelta", StringComparison.OrdinalIgnoreCase))
                            mod.binaryModType = 2;
                        mod.data = File.ReadAllBytes(file);
                        mods.Add(mod);
                    }
                    catch
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Filename not valid: " + relativeFilePath);
                        }
                        continue;
                    }
                }
                else if (file.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase))
                {
                    int indexTpf = -1;
                    IntPtr handle = IntPtr.Zero;
                    ZlibHelper.Zip zip = new ZlibHelper.Zip();
                    try
                    {
                        handle = zip.Open(file, ref numEntries, 1);
                        for (ulong i = 0; i < numEntries; i++)
                        {
                            result = zip.GetCurrentFileInfo(handle, ref fileName, ref dstLen);
                            if (result != 0)
                                throw new Exception();
                            fileName = fileName.Trim();
                            if (Path.GetExtension(fileName).ToLowerInvariant() == ".def" ||
                                Path.GetExtension(fileName).ToLowerInvariant() == ".log")
                            {
                                indexTpf = (int)i;
                                break;
                            }
                            result = zip.GoToNextFile(handle);
                            if (result != 0)
                                throw new Exception();
                        }
                        byte[] listText = new byte[dstLen];
                        result = zip.ReadCurrentFile(handle, listText, dstLen);
                        if (result != 0)
                            throw new Exception();
                        ddsList = Encoding.ASCII.GetString(listText).Trim('\0').Replace("\r", "").TrimEnd('\n').Split('\n');

                        result = zip.GoToFirstFile(handle);
                        if (result != 0)
                            throw new Exception();

                        for (uint i = 0; i < numEntries; i++)
                        {
                            if (i == indexTpf)
                            {
                                result = zip.GoToNextFile(handle);
                                continue;
                            }
                            BinaryMod mod = new BinaryMod();
                            try
                            {
                                uint crc = 0;
                                result = zip.GetCurrentFileInfo(handle, ref fileName, ref dstLen);
                                if (result != 0)
                                    throw new Exception();
                                string filename = Path.GetFileName(fileName).Trim();
                                foreach (string dds in ddsList)
                                {
                                    string ddsFile = dds.Split('|')[1];
                                    if (ddsFile.ToLowerInvariant().Trim() != filename.ToLowerInvariant())
                                        continue;
                                    crc = uint.Parse(dds.Split('|')[0].Substring(2), System.Globalization.NumberStyles.HexNumber);
                                    break;
                                }
                                if (crc == 0)
                                {
                                    if (Path.GetExtension(filename).ToLowerInvariant() != ".def" &&
                                        Path.GetExtension(filename).ToLowerInvariant() != ".log")
                                    {
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Skipping file: " + filename + " not found in definition file, entry: " +
                                                (i + 1) + " - mod: " + relativeFilePath);
                                        }
                                    }
                                    zip.GoToNextFile(handle);
                                    continue;
                                }

                                List<FoundTexture> foundCrcList = textures.FindAll(s => s.crc == crc);
                                if (foundCrcList.Count == 0)
                                {
                                    Console.WriteLine("Texture skipped. File " + filename + string.Format(" - 0x{0:X8}", crc) +
                                        " is not present in your game setup - mod: " + relativeFilePath);
                                    zip.GoToNextFile(handle);
                                    continue;
                                }

                                string textureName = foundCrcList[0].name;
                                mod.textureName = textureName;
                                mod.binaryModType = 0;
                                mod.textureCrc = crc;
                                mod.data = new byte[dstLen];
                                result = zip.ReadCurrentFile(handle, mod.data, dstLen);
                                if (result != 0)
                                {
                                    zip.GoToNextFile(handle);
                                    if (ipc)
                                    {
                                        Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                        Console.Out.Flush();
                                    }
                                    else
                                    {
                                        Console.WriteLine("Error in texture: " + textureName + string.Format("_0x{0:X8}", crc) +
                                            ", skipping texture, entry: " + (i + 1) + " - mod: " + relativeFilePath);
                                    }
                                    continue;
                                }

                                PixelFormat pixelFormat = foundCrcList[0].pixfmt;
                                Image image = new Image(mod.data, Path.GetExtension(filename));

                                if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                                    foundCrcList[0].width / foundCrcList[0].height)
                                {
                                    zip.GoToNextFile(handle);
                                    if (ipc)
                                    {
                                        Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                        Console.Out.Flush();
                                    }
                                    else
                                    {
                                        Console.WriteLine("Error in texture: " + textureName + string.Format("_0x{0:X8}", crc) +
                                            " This texture has wrong aspect ratio, skipping texture, entry: " + (i + 1) + " - mod: " + relativeFilePath);
                                    }
                                    continue;
                                }

                                if (!image.checkDDSHaveAllMipmaps() ||
                                   (foundCrcList[0].list.Find(s => s.path != "").numMips > 1 && image.mipMaps.Count() <= 1) ||
                                    image.pixelFormat != pixelFormat)
                                {
                                    bool dxt1HasAlpha = false;
                                    byte dxt1Threshold = 128;
                                    if (foundCrcList[0].alphadxt1)
                                    {
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]PROCESSING_FILE Converting " + textureName);
                                            Console.Out.Flush();
                                        }
                                        else
                                        {
                                            Console.WriteLine("Converting/correcting texture: " + textureName);
                                        }
                                        dxt1HasAlpha = true;
                                        if (image.pixelFormat == PixelFormat.ARGB ||
                                            image.pixelFormat == PixelFormat.DXT3 ||
                                            image.pixelFormat == PixelFormat.DXT5)
                                        {
                                            Console.WriteLine("Warning for texture: " + textureName + ". This texture converted from full alpha to binary alpha.");
                                        }
                                    }
                                    image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                                    mod.data = image.StoreImageToDDS();
                                }
                                mods.Add(mod);
                            }
                            catch
                            {
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                    Console.Out.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("Skipping not compatible content, entry: " + (i + 1) + " file: " + fileName + " - mod: " + relativeFilePath);
                                }
                            }
                            zip.GoToNextFile(handle);
                        }
                        zip.Close(handle);
                        handle = IntPtr.Zero;
                    }
                    catch
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Mod is not compatible: " + relativeFilePath + Environment.NewLine);
                        }
                        if (handle != IntPtr.Zero)
                            zip.Close(handle);
                        handle = IntPtr.Zero;
                        continue;
                    }
                }
                else if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    BinaryMod mod = new BinaryMod();
                    string filename = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (!filename.Contains("0x"))
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }
                    int idx = filename.IndexOf("0x");
                    if (filename.Length - idx < 10)
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }
                    uint crc;
                    string crcStr = filename.Substring(idx + 2, 8);
                    try
                    {
                        crc = uint.Parse(crcStr, System.Globalization.NumberStyles.HexNumber);
                    }
                    catch
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }

                    List<FoundTexture> foundCrcList = textures.FindAll(s => s.crc == crc);
                    if (foundCrcList.Count == 0)
                    {
                        Console.WriteLine("Texture skipped. Texture " + relativeFilePath + " is not present in your game setup.");
                        continue;
                    }

                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        PixelFormat pixelFormat = foundCrcList[0].pixfmt;

                        mod.data = fs.ReadToBuffer((int)fs.Length);
                        Image image = new Image(mod.data, Image.ImageFormat.DDS);

                        if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                            foundCrcList[0].width / foundCrcList[0].height)
                        {
                            Console.WriteLine("Error in texture: " + relativeFilePath + " This texture has wrong aspect ratio, skipping texture...");
                            continue;
                        }

                        if (!image.checkDDSHaveAllMipmaps() ||
                           (foundCrcList[0].list.Find(s => s.path != "").numMips > 1 && image.mipMaps.Count() <= 1) ||
                            image.pixelFormat != pixelFormat)
                        {
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]PROCESSING_FILE Converting " + Path.GetFileName(file));
                                Console.Out.Flush();
                            }
                            else
                            {
                                Console.WriteLine("Converting/correcting texture: " + relativeFilePath);
                            }
                            bool dxt1HasAlpha = false;
                            byte dxt1Threshold = 128;
                            if (foundCrcList[0].alphadxt1)
                            {
                                dxt1HasAlpha = true;
                                if (image.pixelFormat == PixelFormat.ARGB ||
                                    image.pixelFormat == PixelFormat.DXT3 ||
                                    image.pixelFormat == PixelFormat.DXT5)
                                {
                                    Console.WriteLine("Warning for texture: " + relativeFilePath + ". This texture converted from full alpha to binary alpha.");
                                }
                            }
                            image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                            mod.data = image.StoreImageToDDS();
                        }

                        mod.textureName = foundCrcList[0].name;
                        mod.binaryModType = 0;
                        mod.textureCrc = crc;
                        mods.Add(mod);
                    }
                }
                else if (
                    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    BinaryMod mod = new BinaryMod();
                    string filename = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (!filename.Contains("0x"))
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }
                    int idx = filename.IndexOf("0x");
                    if (filename.Length - idx < 10)
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }
                    uint crc;
                    string crcStr = filename.Substring(idx + 2, 8);
                    try
                    {
                        crc = uint.Parse(crcStr, System.Globalization.NumberStyles.HexNumber);
                    }
                    catch
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Texture filename not valid: " + relativeFilePath + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                        }
                        continue;
                    }

                    List<FoundTexture> foundCrcList = textures.FindAll(s => s.crc == crc);
                    if (foundCrcList.Count == 0)
                    {
                        Console.WriteLine("Texture skipped. Texture " + relativeFilePath + " is not present in your game setup.");
                        continue;
                    }

                    PixelFormat pixelFormat = foundCrcList[0].pixfmt;
                    Image image = new Image(file, Image.ImageFormat.Unknown).convertToARGB();

                    if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                        foundCrcList[0].width / foundCrcList[0].height)
                    {
                        if (ipc)
                        {
                            Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                            Console.Out.Flush();
                        }
                        else
                        {
                            Console.WriteLine("Error in texture: " + relativeFilePath + " This texture has wrong aspect ratio, skipping texture...");
                        }
                        continue;
                    }

                    bool dxt1HasAlpha = false;
                    byte dxt1Threshold = 128;
                    if (foundCrcList[0].alphadxt1)
                    {
                        dxt1HasAlpha = true;
                        if (image.pixelFormat == PixelFormat.ARGB ||
                            image.pixelFormat == PixelFormat.DXT3 ||
                            image.pixelFormat == PixelFormat.DXT5)
                        {
                            Console.WriteLine("Warning for texture: " + relativeFilePath + ". This texture converted from full alpha to binary alpha.");
                        }
                    }
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]PROCESSING_FILE Converting " + Path.GetFileName(file));
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("Converting/correcting texture: " + relativeFilePath);
                    }
                    image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                    mod.data = image.StoreImageToDDS();
                    mod.textureName = foundCrcList[0].name;
                    mod.binaryModType = 0;
                    mod.textureCrc = crc;
                    mods.Add(mod);
                }

                for (int l = 0; l < mods.Count; l++)
                {
                    MipMaps.FileMod fileMod = new MipMaps.FileMod();
                    Stream dst = MipMaps.compressData(mods[l].data);
                    dst.SeekBegin();
                    fileMod.offset = outFs.Position;
                    fileMod.size = dst.Length;

                    if (mods[l].binaryModType == 1)
                    {
                        fileMod.tag = MipMaps.FileBinaryTag;
                        if (mods[l].packagePath.Contains("\\DLC\\"))
                        {
                            string dlcName = mods[l].packagePath.Split('\\')[3];
                            fileMod.name = "D" + dlcName.Length + "-" + dlcName + "-";
                        }
                        else
                        {
                            fileMod.name = "B";
                        }
                        fileMod.name += Path.GetFileName(mods[l].packagePath).Length + "-" + Path.GetFileName(mods[l].packagePath) + "-E" + mods[l].exportId + ".bin";

                        outFs.WriteInt32(mods[l].exportId);
                        outFs.WriteStringASCIINull(mods[l].packagePath);
                    }
                    else if (mods[l].binaryModType == 2)
                    {
                        fileMod.tag = MipMaps.FileXdeltaTag;
                        if (mods[l].packagePath.Contains("\\DLC\\"))
                        {
                            string dlcName = mods[l].packagePath.Split('\\')[3];
                            fileMod.name = "D" + dlcName.Length + "-" + dlcName + "-";
                        }
                        else
                        {
                            fileMod.name = "B";
                        }
                        fileMod.name += Path.GetFileName(mods[l].packagePath).Length + "-" + Path.GetFileName(mods[l].packagePath) + "-E" + mods[l].exportId + ".xdelta";

                        outFs.WriteInt32(mods[l].exportId);
                        outFs.WriteStringASCIINull(mods[l].packagePath);
                    }
                    else
                    {
                        fileMod.tag = MipMaps.FileTextureTag;
                        fileMod.name = mods[l].textureName + string.Format("_0x{0:X8}", mods[l].textureCrc) + ".dds";
                        outFs.WriteStringASCIINull(mods[l].textureName);
                        outFs.WriteUInt32(mods[l].textureCrc);
                    }
                    outFs.WriteFromStream(dst, dst.Length);
                    modFiles.Add(fileMod);
                }
                mods.Clear();
            }

            if (modFiles.Count == 0)
            {
                outFs.Close();
                if (File.Exists(memFilePath))
                    File.Delete(memFilePath);
                if (ipc)
                {
                    Console.WriteLine("[IPC]ERROR_NO_BUILDABLE_FILES");
                    Console.Out.Flush();
                }
                return false;
            }

            long pos = outFs.Position;
            outFs.SeekBegin();
            outFs.WriteUInt32(TextureModTag);
            outFs.WriteUInt32(TextureModVersion);
            outFs.WriteInt64(pos);
            outFs.JumpTo(pos);
            outFs.WriteUInt32((uint)gameId);
            outFs.WriteInt32(modFiles.Count);
            for (int i = 0; i < modFiles.Count; i++)
            {
                outFs.WriteUInt32(modFiles[i].tag);
                outFs.WriteStringASCIINull(modFiles[i].name);
                outFs.WriteInt64(modFiles[i].offset);
                outFs.WriteInt64(modFiles[i].size);
            }

            outFs.Close();

            return true;
        }

        static public bool ConvertToMEM(MeType gameId, string inputDir, string memFile, bool ipc)
        {
            return convertDataModtoMem(inputDir, memFile, gameId, ipc);
        }

        static public bool convertGameTexture(string inputFile, string outputFile)
        {
            string filename = Path.GetFileNameWithoutExtension(inputFile).ToLowerInvariant();
            if (!filename.Contains("0x"))
            {
                Console.WriteLine("Texture filename not valid: " + Path.GetFileName(inputFile) + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                return false;
            }
            int idx = filename.IndexOf("0x");
            if (filename.Length - idx < 10)
            {
                Console.WriteLine("Texture filename not valid: " + Path.GetFileName(inputFile) + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                return false;
            }
            uint crc;
            string crcStr = filename.Substring(idx + 2, 8);
            try
            {
                crc = uint.Parse(crcStr, System.Globalization.NumberStyles.HexNumber);
            }
            catch
            {
                Console.WriteLine("Texture filename not valid: " + Path.GetFileName(inputFile) + " Texture filename must include texture CRC (0xhhhhhhhh). Skipping texture...");
                return false;
            }

            List<FoundTexture> foundCrcList = textures.FindAll(s => s.crc == crc);
            if (foundCrcList.Count == 0)
            {
                Console.WriteLine("Texture skipped. Texture " + Path.GetFileName(inputFile) + " is not present in your game setup.");
                return false;
            }

            PixelFormat pixelFormat = foundCrcList[0].pixfmt;
            Image image = new Image(inputFile);

            if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                foundCrcList[0].width / foundCrcList[0].height)
            {
                Console.WriteLine("Error in texture: " + Path.GetFileName(inputFile) + " This texture has wrong aspect ratio, skipping texture...");
                return false;
            }

            bool dxt1HasAlpha = false;
            byte dxt1Threshold = 128;
            if (foundCrcList[0].alphadxt1)
            {
                dxt1HasAlpha = true;
                if (image.pixelFormat == PixelFormat.ARGB ||
                    image.pixelFormat == PixelFormat.DXT3 ||
                    image.pixelFormat == PixelFormat.DXT5)
                {
                    Console.WriteLine("Warning for texture: " + Path.GetFileName(inputFile) + ". This texture converted from full alpha to binary alpha.");
                }
            }
            image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            using (FileStream fs = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write))
            {
                fs.WriteFromBuffer(image.StoreImageToDDS());
            }

            return true;
        }

        static public bool convertGameImage(MeType gameId, string inputFile, string outputFile)
        {
            loadTexturesMap(gameId);

            bool status = convertGameTexture(inputFile, outputFile);
            if (!status)
            {
                Console.WriteLine("Error: Some errors have occured.");
            }

            return status;
        }

        static public bool convertGameImages(MeType gameId, string inputDir, string outputDir)
        {
            loadTexturesMap(gameId);

            List<string> list = Directory.GetFiles(inputDir, "*.dds").Where(item => item.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)).ToList();
            list.AddRange(Directory.GetFiles(inputDir, "*.png").Where(item => item.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
            list.AddRange(Directory.GetFiles(inputDir, "*.bmp").Where(item => item.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)));
            list.AddRange(Directory.GetFiles(inputDir, "*.tga").Where(item => item.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)));
            list.AddRange(Directory.GetFiles(inputDir, "*.jpg").Where(item => item.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)));
            list.AddRange(Directory.GetFiles(inputDir, "*.jpeg").Where(item => item.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)));
            list.Sort();

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            bool status = true;
            foreach (string file in list)
            {
                if (!convertGameTexture(file, Path.Combine(outputDir, Path.GetFileNameWithoutExtension(file) + ".dds")))
                    status = false;
            }

            return status;
        }

        static public bool convertImage(string inputFile, string outputFile, string format, string threshold)
        {
            format = format.ToLowerInvariant();
            PixelFormat pixFmt;
            bool dxt1HasAlpha = false;
            byte dxt1Threshold = 128;
            try
            {
                dxt1Threshold = byte.Parse(threshold);
            }
            catch
            {
                Console.WriteLine("Error: wrong threshold for dxt1: " + threshold);
                return false;
            }

            switch (format)
            {
                case "dxt1":
                    pixFmt = PixelFormat.DXT1;
                    break;
                case "dxt1a":
                    pixFmt = PixelFormat.DXT1;
                    dxt1HasAlpha = true;
                    break;
                case "dxt3":
                    pixFmt = PixelFormat.DXT3;
                    break;
                case "dxt5":
                    pixFmt = PixelFormat.DXT5;
                    break;
                case "ati2":
                    pixFmt = PixelFormat.ATI2;
                    break;
                case "v8u8":
                    pixFmt = PixelFormat.V8U8;
                    break;
                case "argb":
                    pixFmt = PixelFormat.ARGB;
                    break;
                case "rgb":
                    pixFmt = PixelFormat.RGB;
                    break;
                case "g8":
                    pixFmt = PixelFormat.G8;
                    break;
                default:
                    Console.WriteLine("Error: not supported format: " + format);
                    return false;

            }
            Image image = new Image(inputFile);
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            image.correctMips(pixFmt, dxt1HasAlpha, dxt1Threshold);
            using (FileStream fs = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write))
            {
                fs.WriteFromBuffer(image.StoreImageToDDS());
            }

            return true;
        }

        static public bool extractTPF(string inputDir, string outputDir, bool ipc)
        {
            Console.WriteLine("Extract TPF files started...");

            bool status = true;
            string[] files = null;
            int result;
            string fileName = "";
            ulong dstLen = 0;
            ulong numEntries = 0;
            List<string> list = Directory.GetFiles(inputDir, "*.tpf").Where(item => item.EndsWith(".tpf", StringComparison.OrdinalIgnoreCase)).ToList();
            list.Sort();
            files = list.ToArray();

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            foreach (string file in files)
            {
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + file);
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("Extract TPF: " + file);
                }
                string outputTPFdir = outputDir + "\\" + Path.GetFileNameWithoutExtension(file);
                if (!Directory.Exists(outputTPFdir))
                    Directory.CreateDirectory(outputTPFdir);

                IntPtr handle = IntPtr.Zero;
                ZlibHelper.Zip zip = new ZlibHelper.Zip();
                try
                {
                    handle = zip.Open(file, ref numEntries, 1);
                    if (handle == IntPtr.Zero)
                        throw new Exception();

                    for (uint i = 0; i < numEntries; i++)
                    {
                        try
                        {
                            result = zip.GetCurrentFileInfo(handle, ref fileName, ref dstLen);
                            if (result != 0)
                                throw new Exception();
                            string filename = Path.GetFileName(fileName).Trim();
                            if (Path.GetExtension(filename).ToLowerInvariant() == ".def" ||
                                Path.GetExtension(filename).ToLowerInvariant() == ".log")
                            {
                                zip.GoToNextFile(handle);
                                continue;
                            }

                            byte[] data = new byte[dstLen];
                            result = zip.ReadCurrentFile(handle, data, dstLen);
                            if (result != 0)
                            {
                                throw new Exception();
                            }
                            if (File.Exists(Path.Combine(outputTPFdir, filename)))
                                File.Delete(Path.Combine(outputTPFdir, filename));
                            using (FileStream fs = new FileStream(Path.Combine(outputTPFdir, filename), FileMode.CreateNew))
                            {
                                fs.WriteFromBuffer(data);
                            }
                            Console.WriteLine(Path.Combine(outputTPFdir, filename));
                        }
                        catch
                        {
                            Console.WriteLine("Skipping damaged content, entry: " + (i + 1) + " file: " + fileName + " - mod: " + file);
                            status = false;
                        }
                        zip.GoToNextFile(handle);
                    }
                    zip.Close(handle);
                    handle = IntPtr.Zero;
                }
                catch
                {
                    Console.WriteLine("TPF file is damaged: " + file);
                    if (handle != IntPtr.Zero)
                        zip.Close(handle);
                    handle = IntPtr.Zero;
                    continue;
                }
            }

            Console.WriteLine("Extract TPF files completed.");
            return status;
        }

        static public bool extractMOD(MeType gameId, string inputDir, string outputDir, bool ipc)
        {
            loadTexturesMap(gameId);

            Console.WriteLine("Extract MOD files started...");

            bool status = true;
            string[] files = null;
            ulong numEntries = 0;
            List<string> list = Directory.GetFiles(inputDir, "*.mod").Where(item => item.EndsWith(".mod", StringComparison.OrdinalIgnoreCase)).ToList();
            list.Sort();
            files = list.ToArray();

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            foreach (string file in files)
            {
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + file);
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("Extract MOD: " + file);
                }
                string outputMODdir = outputDir + "\\" + Path.GetFileNameWithoutExtension(file);
                if (!Directory.Exists(outputMODdir))
                    Directory.CreateDirectory(outputMODdir);

                try
                {
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        uint textureCrc;
                        int len = fs.ReadInt32();
                        string version = fs.ReadStringASCIINull();
                        if (version.Length < 5) // legacy .mod
                            fs.SeekBegin();
                        else
                        {
                            fs.SeekBegin();
                            len = fs.ReadInt32();
                            version = fs.ReadStringASCII(len); // version
                        }
                        numEntries = fs.ReadUInt32();
                        for (uint i = 0; i < numEntries; i++)
                        {
                            len = fs.ReadInt32();
                            string desc = fs.ReadStringASCII(len); // description
                            len = fs.ReadInt32();
                            string scriptLegacy = fs.ReadStringASCII(len);
                            if (desc.Contains("Binary Replacement"))
                            {
                                int exportId = -1;
                                string path = "";
                                string package = "";
                                try
                                {
                                    Misc.ParseME3xBinaryScriptMod(scriptLegacy, ref package, ref exportId, ref path);
                                    if (exportId == -1 || package == "" || path == "")
                                        throw new Exception();
                                }
                                catch
                                {
                                    len = fs.ReadInt32();
                                    fs.Skip(len);
                                    Console.WriteLine("Skipping not compatible content, entry: " + (i + 1) + " - mod: " + file);
                                    status = false;
                                    continue;
                                }
                                path = Path.Combine(path, package);
                                len = fs.ReadInt32();
                                string newFilename;
                                if (path.Contains("\\DLC\\"))
                                {
                                    string dlcName = path.Split('\\')[3];
                                    newFilename = "D" + dlcName.Length + "-" + dlcName + "-";
                                }
                                else
                                {
                                    newFilename = "B";
                                }
                                newFilename += Path.GetFileName(path).Length + "-" + Path.GetFileName(path) + "-E" + exportId + ".bin";
                                if (File.Exists(Path.Combine(outputMODdir, newFilename)))
                                    File.Delete(Path.Combine(outputMODdir, newFilename));
                                using (FileStream fs2 = new FileStream(Path.Combine(outputMODdir, newFilename), FileMode.CreateNew))
                                {
                                    fs2.WriteFromStream(fs, len);
                                }
                                Console.WriteLine(Path.Combine(outputMODdir, newFilename));
                            }
                            else
                            {
                                string textureName = desc.Split(' ').Last();
                                FoundTexture f;
                                try
                                {
                                    f = Misc.ParseLegacyMe3xScriptMod(textures, scriptLegacy, textureName);
                                    textureCrc = f.crc;
                                    if (textureCrc == 0)
                                        throw new Exception();
                                }
                                catch
                                {
                                    len = fs.ReadInt32();
                                    fs.Skip(len);
                                    Console.WriteLine("Skipping not compatible content, entry: " + (i + 1) + " - mod: " + file);
                                    status = false;
                                    continue;
                                }
                                len = fs.ReadInt32();
                                string newFile = textureName + string.Format("_0x{0:X8}", f.crc) + ".dds";
                                if (File.Exists(Path.Combine(outputMODdir, newFile)))
                                    File.Delete(Path.Combine(outputMODdir, newFile));
                                using (FileStream fs2 = new FileStream(Path.Combine(outputMODdir, newFile), FileMode.CreateNew))
                                {
                                    fs2.WriteFromStream(fs, len);
                                }
                                Console.WriteLine(Path.Combine(outputMODdir, newFile));
                            }
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("MOD is not compatible: " + file);
                    status = false;
                    continue;
                }
            }

            Console.WriteLine("Extract MOD files completed.");
            return status;
        }

        static public bool extractMEM(MeType gameId, string inputDir, string outputDir, bool ipc)
        {
            loadTexturesMap(gameId);

            Console.WriteLine("Extract MEM files started...");

            string[] files = null;
            List<string> list = Directory.GetFiles(inputDir, "*.mem").Where(item => item.EndsWith(".mem", StringComparison.OrdinalIgnoreCase)).ToList();
            list.Sort();
            files = list.ToArray();

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            int currentNumberOfTotalMods = 1;
            int totalNumberOfMods = 0;
            for (int i = 0; i < files.Count(); i++)
            {
                using (FileStream fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                {
                    uint tag = fs.ReadUInt32();
                    uint version = fs.ReadUInt32();
                    if (tag != TextureModTag || version != TextureModVersion)
                        continue;
                    fs.JumpTo(fs.ReadInt64());
                    fs.SkipInt32();
                    totalNumberOfMods += fs.ReadInt32();
                }
            }

            int lastProgress = -1;
            foreach (string file in files)
            {
                string relativeFilePath = file.Substring(inputDir.TrimEnd('\\').Length + 1);
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + relativeFilePath);
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("Extract MEM: " + relativeFilePath);
                }
                string outputMODdir = outputDir + "\\" + Path.GetFileNameWithoutExtension(file);
                if (!Directory.Exists(outputMODdir))
                    Directory.CreateDirectory(outputMODdir);

                try
                {
                    using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                    {
                        uint tag = fs.ReadUInt32();
                        uint version = fs.ReadUInt32();
                        if (tag != TextureModTag || version != TextureModVersion)
                        {
                            if (version != TextureModVersion)
                            {
                                Console.WriteLine("File " + relativeFilePath + " was made with an older version of MEM, skipping...");
                            }
                            else
                            {
                                Console.WriteLine("File " + relativeFilePath + " is not a valid MEM mod, skipping...");
                            }
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                Console.Out.Flush();
                            }
                            continue;
                        }
                        else
                        {
                            uint gameType = 0;
                            fs.JumpTo(fs.ReadInt64());
                            gameType = fs.ReadUInt32();
                            if (textures != null && (MeType)gameType != gameId)
                            {
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                    Console.Out.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("File " + relativeFilePath + " is not a MEM mod valid for this game");
                                }
                                continue;
                            }
                        }
                        int numFiles = fs.ReadInt32();
                        List<MipMaps.FileMod> modFiles = new List<MipMaps.FileMod>();
                        for (int i = 0; i < numFiles; i++)
                        {
                            MipMaps.FileMod fileMod = new MipMaps.FileMod();
                            fileMod.tag = fs.ReadUInt32();
                            fileMod.name = fs.ReadStringASCIINull();
                            fileMod.offset = fs.ReadInt64();
                            fileMod.size = fs.ReadInt64();
                            modFiles.Add(fileMod);
                        }
                        numFiles = modFiles.Count;

                        for (int i = 0; i < numFiles; i++, currentNumberOfTotalMods++)
                        {
                            string name = "";
                            uint crc = 0;
                            long size = 0, dstLen = 0;
                            int exportId = -1;
                            string pkgPath = "";
                            byte[] dst = null;
                            fs.JumpTo(modFiles[i].offset);
                            size = modFiles[i].size;
                            if (modFiles[i].tag == FileTextureTag)
                            {
                                name = fs.ReadStringASCIINull();
                                crc = fs.ReadUInt32();
                            }
                            else if (modFiles[i].tag == MipMaps.FileBinaryTag)
                            {
                                name = modFiles[i].name;
                                exportId = fs.ReadInt32();
                                pkgPath = fs.ReadStringASCIINull();
                            }

                            if (!ipc)
                            {
                                Console.WriteLine("Processing MEM mod " + relativeFilePath +
                                        " - File " + (i + 1) + " of " + numFiles + " - " + name);
                            }
                            int newProgress = currentNumberOfTotalMods * 100 / totalNumberOfMods;
                            if (ipc && lastProgress != newProgress)
                            {
                                Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                                Console.Out.Flush();
                                lastProgress = newProgress;
                            }

                            dst = MipMaps.decompressData(fs, size);
                            dstLen = dst.Length;

                            if (modFiles[i].tag == FileTextureTag)
                            {
                                string filename = name + "_" + string.Format("0x{0:X8}", crc) + ".dds";
                                using (FileStream output = new FileStream(Path.Combine(outputMODdir, Path.GetFileName(filename)), FileMode.Create, FileAccess.Write))
                                {
                                    output.Write(dst, 0, (int)dstLen);
                                }
                            }
                            else if (modFiles[i].tag == MipMaps.FileBinaryTag)
                            {
                                string path = pkgPath;
                                string newFilename;
                                if (path.Contains("\\DLC\\"))
                                {
                                    string dlcName = path.Split('\\')[3];
                                    newFilename = "D" + dlcName.Length + "-" + dlcName + "-";
                                }
                                else
                                {
                                    newFilename = "B";
                                }
                                newFilename += Path.GetFileName(path).Length + "-" + Path.GetFileName(path) + "-E" + exportId + ".bin";
                                using (FileStream output = new FileStream(Path.Combine(outputMODdir, newFilename), FileMode.Create, FileAccess.Write))
                                {
                                    output.Write(dst, 0, (int)dstLen);
                                }
                            }
                            else if (modFiles[i].tag == MipMaps.FileXdeltaTag)
                            {
                                string path = pkgPath;
                                string newFilename;
                                if (path.Contains("\\DLC\\"))
                                {
                                    string dlcName = path.Split('\\')[3];
                                    newFilename = "D" + dlcName.Length + "-" + dlcName + "-";
                                }
                                else
                                {
                                    newFilename = "B";
                                }
                                newFilename += Path.GetFileName(path).Length + "-" + Path.GetFileName(path) + "-E" + exportId + ".xdelta";
                                using (FileStream output = new FileStream(Path.Combine(outputMODdir, newFilename), FileMode.Create, FileAccess.Write))
                                {
                                    output.Write(dst, 0, (int)dstLen);
                                }
                            }
                            else
                            {
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                                    Console.Out.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("Unknown tag for file: " + name);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR_FILE_NOT_COMPATIBLE " + relativeFilePath);
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("MEM mod is not compatible: " + file);
                    }
                    continue;
                }
            }

            Console.WriteLine("Extract MEM mod files completed.");
            return true;
        }

        public static bool ApplyModTag(MeType gameId, int alotV, int meuitmV)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            return applyModTag((int)gameId, meuitmV, alotV);
        }

        public static bool ApplyME1LAAPatch()
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(MeType.ME1_TYPE, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            if (!Misc.ApplyLAAForME1Exe())
                return false;
            if (!Misc.ChangeProductNameForME1Exe())
                return false;

            return true;
        }

        public static bool ApplyLODAndGfxSettings(MeType gameId, bool softShadowsME1, bool meuitmMode)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            string path = gameData.EngineConfigIniPath;
            bool exist = File.Exists(path);
            if (!exist)
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            ConfIni engineConf = new ConfIni(path);
            LODSettings.removeLOD(gameId, engineConf);
            LODSettings.updateGFXSettings(gameId, engineConf, softShadowsME1, meuitmMode);

            return true;
        }

        public static bool RemoveLODSettings(MeType gameId)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            string path = gameData.EngineConfigIniPath;
            bool exist = File.Exists(path);
            if (!exist)
                return true;
            ConfIni engineConf = new ConfIni(path);
            LODSettings.removeLOD(gameId, engineConf);

            return true;
        }

        public static bool PrintLODSettings(MeType gameId)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            string path = gameData.EngineConfigIniPath;
            bool exist = File.Exists(path);
            if (!exist)
                return true;
            ConfIni engineConf = new ConfIni(path);
            string log = "";
            LODSettings.readLOD(gameId, engineConf, ref log);
            Console.WriteLine(log);

            return true;
        }

        public static bool CheckGameData(MeType gameId, bool wihtoutSfars, bool onlyVanilla, bool backupMode, bool ipc)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }
            string errors = "";
            List<string> modList = new List<string>();
            bool vanilla = Misc.checkGameFiles(gameId, ref errors, ref modList, ipc, wihtoutSfars, onlyVanilla, backupMode);

            if (!ipc)
            {
                Console.WriteLine(errors);
                if (modList.Count != 0)
                {
                    Console.WriteLine(Environment.NewLine + "------- Detected mods --------" + Environment.NewLine);
                    for (int l = 0; l < modList.Count; l++)
                    {
                        Console.WriteLine(modList[l] + Environment.NewLine);
                    }
                    Console.WriteLine("------------------------------" + Environment.NewLine + Environment.NewLine);
                }
            }

            if (!vanilla && !ipc)
            {
                Console.WriteLine("===========================================================================" + Environment.NewLine);
                Console.WriteLine("WARNING: looks like the following file(s) are not vanilla or not recognized" + Environment.NewLine);
                Console.WriteLine("===========================================================================" + Environment.NewLine + Environment.NewLine);
                Console.WriteLine(errors);
            }

            return vanilla;
        }

        public static bool DetectBadMods(MeType gameId, bool ipc)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            List<string> badMods = Misc.detectBrokenMod(gameId);
            if (badMods.Count != 0)
            {
                if (!ipc)
                    Console.WriteLine("Error: Detected not compatible mods: \n\n");
                for (int l = 0; l < badMods.Count; l++)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR " + badMods[l]);
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine(badMods[l] + Environment.NewLine);
                    }
                }
            }

            return true;
        }

        public static bool DetectMods(MeType gameId, bool ipc)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            List<string> mods = Misc.detectMods(gameId);
            if (mods.Count != 0)
            {
                if (!ipc)
                    Console.WriteLine("Detected compatible mods:");
                for (int l = 0; l < mods.Count; l++)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]MOD " + mods[l]);
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine(mods[l] + Environment.NewLine);
                    }
                }
            }

            return true;
        }

        static private bool detectMod(int gameId)
        {
            string path;
            if (gameId == 1)
                path = GameData.GamePath + @"\BioGame\CookedPC\testVolumeLight_VFX.upk";
            else if (gameId == 2)
                path = GameData.GamePath + @"\BioGame\CookedPC\BIOC_Materials.pcc";
            else
                path = GameData.GamePath + @"\BIOGame\CookedPCConsole\adv_combat_tutorial_xbox_D_Int.afc";
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    fs.Seek(-4, SeekOrigin.End);
                    if (fs.ReadUInt32() == MEMI_TAG)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool ScanTextures(MeType gameId, bool ipc, bool repack = false)
        {
            Console.WriteLine("Scan textures started...");
            treeScan.PrepareListOfTextures(gameId, ipc);
            textures = treeScan.treeScan;
            Console.WriteLine("Scan textures finished.\n");

            return true;
        }

        static private void RepackME23(MeType gameId, bool ipc)
        {
            Console.WriteLine("Repack started...");
            if (ipc)
            {
                Console.WriteLine("[IPC]STAGE_CONTEXT STAGE_REPACK");
                Console.Out.Flush();
            }

            if (gameId == MeType.ME2_TYPE)
                pkgsToRepack.Remove(GameData.GamePath + @"\BioGame\CookedPC\BIOC_Materials.pcc");
            int lastProgress = -1;
            for (int i = 0; i < pkgsToRepack.Count; i++)
            {
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + pkgsToRepack[i]);
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("File: " + pkgsToRepack[i]);
                }
                int newProgress = (i * 100 / pkgsToRepack.Count);
                if (ipc && lastProgress != newProgress)
                {
                    Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                    Console.Out.Flush();
                    lastProgress = newProgress;
                }
                try
                {
                    Package package = new Package(pkgsToRepack[i], false, true);
                    if (package.compressed &&
                        (gameId == MeType.ME2_TYPE && package.compressionType != Package.CompressionType.Zlib))
                    {
                        package.Dispose();
                        package = new Package(pkgsToRepack[i]);
                        package.SaveToFile(true);
                    }
                    else if ((gameId == MeType.ME2_TYPE || gameId == MeType.ME3_TYPE) && !package.compressed)
                    {
                        package.Dispose();
                        package = new Package(pkgsToRepack[i]);
                        package.SaveToFile(true, true);
                    }
                    package.Dispose();
                }
                catch
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR Error opening package file: " + GameData.RelativeGameData(pkgsToRepack[i]));
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("Error opening package file: " + GameData.RelativeGameData(pkgsToRepack[i]));
                    }
                }
                if (gameId == MeType.ME3_TYPE)
                    TOCBinFile.UpdateAllTOCBinFiles();
            }
            Console.WriteLine("Repack finished.\n");
        }

        public static bool InstallMods(MeType gameId, string inputDir, bool ipc, bool repack, bool guiInstaller)
        {
            textures = new List<FoundTexture>();
            treeScan = new TreeScan();
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            if (!guiInstaller)
            {
                Console.WriteLine("Getting started...\n");
                if (gameId == MeType.ME1_TYPE && !File.Exists(gameData.EngineConfigIniPath))
                {
                    Console.WriteLine("Error: Missing game configuration file.\nYou need atleast once launch the game first.");
                    return false;
                }

                bool writeAccess = CheckAndCorrectAccessToGame(gameId);
                if (!writeAccess)
                {
                    Console.WriteLine("Error: Detected no write access to game folders");
                    return false;
                }

                List<string> badMods = Misc.detectBrokenMod(gameId);
                if (badMods.Count != 0)
                {
                    Console.WriteLine("Error: Detected not compatible mods: \n\n");
                    for (int l = 0; l < badMods.Count; l++)
                    {
                        Console.WriteLine(badMods[l] + Environment.NewLine);
                    }
                    return false;
                }
            }

            if (gameId == MeType.ME1_TYPE)
                repack = false;

            bool modded = detectMod((int)gameId);
            bool unpackNeeded = false;
            if (gameId == MeType.ME3_TYPE && !modded)
                unpackNeeded = Misc.unpackSFARisNeeded();
            if (ipc)
            {
                if (!modded)
                {
                    if (gameId == MeType.ME3_TYPE && unpackNeeded)
                        Console.WriteLine("[IPC]STAGE_ADD STAGE_UNPACKDLC");
                    Console.WriteLine("[IPC]STAGE_ADD STAGE_PRESCAN");
                    Console.WriteLine("[IPC]STAGE_ADD STAGE_SCAN");
                }
                Console.WriteLine("[IPC]STAGE_ADD STAGE_INSTALLTEXTURES");
                Console.WriteLine("[IPC]STAGE_ADD STAGE_SAVING");
                if (!modded && repack)
                    Console.WriteLine("[IPC]STAGE_ADD STAGE_REPACK");
                Console.Out.Flush();
            }

            if (gameId == MeType.ME3_TYPE && !modded && unpackNeeded)
            {
                Console.WriteLine("Unpacking DLCs started...");
                if (ipc)
                {
                    Console.WriteLine("[IPC]STAGE_CONTEXT STAGE_UNPACKDLC");
                    Console.Out.Flush();
                }

                ME3DLC.unpackAllDLC(ipc);

                Console.WriteLine("Unpacking DLCs finished.\n");
            }

            gameData.getPackages();
            if (gameId != MeType.ME1_TYPE)
                gameData.getTfcTextures();

            if (!modded)
            {
                if (repack)
                {
                    pkgsToRepack = new List<string>();
                    for (int i = 0; i < GameData.packageFiles.Count; i++)
                    {
                        pkgsToRepack.Add(GameData.packageFiles[i]);
                    }
                }

                ScanTextures(gameId, ipc, repack);
            }


            Console.WriteLine("Process textures started...");
            if (ipc)
            {
                Console.WriteLine("[IPC]STAGE_CONTEXT STAGE_INSTALLTEXTURES");
                Console.Out.Flush();
            }
            List<string> modFiles = Directory.GetFiles(inputDir, "*.mem").Where(item => item.EndsWith(".mem", StringComparison.OrdinalIgnoreCase)).ToList();
            if (modded)
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Program.MAINEXENAME);
                string mapFile = Path.Combine(path, "me" + (int)gameId + "map.bin");
                if (!loadTexturesMapFile(mapFile, ipc))
                    return false;
            }
            bool status = applyMods(modFiles, repack, ipc);


            if (!modded && repack)
                RepackME23(gameId, ipc);


            if (!guiInstaller)
            {
                if (!applyModTag((int)gameId, 0, 0))
                    Console.WriteLine("Failed applying stamp for installation!");

                Console.WriteLine("Updating LODs and other settings started...");
                string path = gameData.EngineConfigIniPath;
                bool exist = File.Exists(path);
                if (!exist)
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                ConfIni engineConf = new ConfIni(path);
                LODSettings.removeLOD(gameId, engineConf);
                LODSettings.updateGFXSettings(gameId, engineConf, false, false);
                Console.WriteLine("Updating LODs and other settings finished");
            }

            if (ipc)
            {
                Console.WriteLine("[IPC]STAGE_CONTEXT STAGE_DONE");
                Console.Out.Flush();
            }

            Console.WriteLine("\nInstallation finished.");

            return true;
        }

        static public bool applyMEMSpecialModME3(string memFile, string tfcName, byte[] guid)
        {
            textures = new List<FoundTexture>();
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(MeType.ME3_TYPE, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            gameData.getPackages();
            gameData.getTfcTextures();

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Program.MAINEXENAME);
            string mapFile = Path.Combine(path, "me3map.bin");
            if (!loadTexturesMapFile(mapFile, false))
                return false;

            List<string> memFiles = new List<string>();
            memFiles.Add(memFile);

            applyMods(memFiles, false, false, true, tfcName, guid);

            return true;
        }

        static public bool applyMods(List<string> files, bool repack, bool ipc, bool special = false, string tfcName = "", byte[] guid = null)
        {
            bool status = true;
            CachePackageMgr cachePackageMgr = new CachePackageMgr();

            int totalNumberOfMods = 0;
            int currentNumberOfTotalMods = 1;

            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                    {
                        uint tag = fs.ReadUInt32();
                        uint version = fs.ReadUInt32();
                        if (tag != TextureModTag || version != TextureModVersion)
                            continue;
                        fs.JumpTo(fs.ReadInt64());
                        fs.SkipInt32();
                        totalNumberOfMods += fs.ReadInt32();
                    }
                }
                catch
                {
                    continue;
                }
            }

            int lastProgress = -1;
            for (int i = 0; i < files.Count; i++)
            {
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + files[i]);
                    Console.Out.Flush();
                }
                else
                {
                    Console.WriteLine("Mod: " + (i + 1) + " of " + files.Count + " started: " + Path.GetFileName(files[i]));
                }

                try
                {
                    using (FileStream fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                    {
                        uint tag = fs.ReadUInt32();
                        uint version = fs.ReadUInt32();
                        if (tag != TextureModTag || version != TextureModVersion)
                        {
                            if (version != TextureModVersion)
                            {
                                Console.WriteLine("File " + files[i] + " was made with an older version of MEM, skipping..." + Environment.NewLine);
                            }
                            else
                            {
                                Console.WriteLine("File " + files[i] + " is not a valid MEM mod, skipping..." + Environment.NewLine);
                            }
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]ERROR Bad MEM mod " + files[i]);
                                Console.Out.Flush();
                            }
                            continue;
                        }
                        else
                        {
                            uint gameType = 0;
                            fs.JumpTo(fs.ReadInt64());
                            gameType = fs.ReadUInt32();
                            if ((MeType)gameType != GameData.gameType)
                            {
                                Console.WriteLine("File " + files[i] + " is not a MEM mod valid for this game, skipping..." + Environment.NewLine);
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR Bad MEM mod " + files[i]);
                                    Console.Out.Flush();
                                }
                                continue;
                            }
                        }
                        int numFiles = fs.ReadInt32();
                        List<MipMaps.FileMod> modFiles = new List<MipMaps.FileMod>();
                        for (int k = 0; k < numFiles; k++)
                        {
                            MipMaps.FileMod fileMod = new MipMaps.FileMod();
                            fileMod.tag = fs.ReadUInt32();
                            fileMod.name = fs.ReadStringASCIINull();
                            fileMod.offset = fs.ReadInt64();
                            fileMod.size = fs.ReadInt64();
                            modFiles.Add(fileMod);
                        }
                        numFiles = modFiles.Count;
                        for (int l = 0; l < numFiles; l++, currentNumberOfTotalMods++)
                        {
                            string name = "";
                            uint crc = 0;
                            long size = 0, dstLen = 0;
                            int exportId = -1;
                            string pkgPath = "";
                            byte[] dst = null;
                            fs.JumpTo(modFiles[l].offset);
                            size = modFiles[l].size;
                            if (modFiles[l].tag == MipMaps.FileTextureTag)
                            {
                                name = fs.ReadStringASCIINull();
                                crc = fs.ReadUInt32();
                            }
                            else if (modFiles[l].tag == MipMaps.FileBinaryTag)
                            {
                                name = modFiles[l].name;
                                exportId = fs.ReadInt32();
                                pkgPath = fs.ReadStringASCIINull();
                            }

                            if (ipc)
                            {
                                Console.WriteLine("[IPC]PROCESSING_TEXTURE_INSTALL " + modFiles[l].name);
                                Console.Out.Flush();
                            }
                            int newProgress = currentNumberOfTotalMods * 100 / totalNumberOfMods;
                            if (ipc && lastProgress != newProgress)
                            {
                                Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                                Console.Out.Flush();
                                lastProgress = newProgress;
                            }

                            dst = MipMaps.decompressData(fs, size);
                            dstLen = dst.Length;

                            if (modFiles[l].tag == MipMaps.FileTextureTag)
                            {
                                int index = -1;
                                for (int t = 0; t < textures.Count; t++)
                                {
                                    if (textures[t].crc == crc)
                                    {
                                        index = t;
                                        break;
                                    }
                                }
                                if (index != -1)
                                {
                                    FoundTexture foundTexture = textures[index];
                                    Image image = new Image(dst, Image.ImageFormat.DDS);
                                    if (!image.checkDDSHaveAllMipmaps())
                                    {
                                        Console.WriteLine("Error in texture: " + name + string.Format("_0x{0:X8}", crc) + " Texture skipped. This texture has not all the required mipmaps" + Environment.NewLine);
                                        continue;
                                    }
                                    string errors = "";
                                    if (special)
                                        errors = replaceTextureSpecialME3Mod(image, foundTexture.list, cachePackageMgr, foundTexture.name, crc, tfcName, guid);
                                    else
                                        errors = new MipMaps().replaceTexture(image, foundTexture.list, cachePackageMgr, foundTexture.name, crc, false);
                                    if (errors != "")
                                    {
                                        if (ipc)
                                        {
                                            Console.WriteLine("[IPC]ERROR Error while replacing texture " + foundTexture.name);
                                            Console.Out.Flush();
                                        }
                                        Console.WriteLine(errors);
                                    }
                                    textures[index] = foundTexture;
                                }
                                else
                                {
                                    Console.WriteLine("Texture skipped. Texture " + name + string.Format("_0x{0:X8}", crc) + " is not present in your game setup" + Environment.NewLine);
                                }
                            }
                            else if (modFiles[l].tag == MipMaps.FileBinaryTag)
                            {
                                string path = GameData.GamePath + pkgPath;
                                if (!File.Exists(path))
                                {
                                    Console.WriteLine("Warning: File " + path + " not exists in your game setup." + Environment.NewLine);
                                    continue;
                                }
                                Package pkg = cachePackageMgr.OpenPackage(path);
                                pkg.setExportData(exportId, dst);
                            }
                            else if (modFiles[l].tag == MipMaps.FileXdeltaTag)
                            {
                                string path = GameData.GamePath + pkgPath;
                                if (!File.Exists(path))
                                {
                                    Console.WriteLine("Warning: File " + path + " not exists in your game setup." + Environment.NewLine);
                                    continue;
                                }
                                Package pkg = cachePackageMgr.OpenPackage(path);
                                byte[] buffer = new Xdelta3Helper.Xdelta3().Decompress(pkg.getExportData(exportId), dst);
                                pkg.setExportData(exportId, buffer);
                            }
                            else
                            {
                                Console.WriteLine("Unknown tag for file: " + name + Environment.NewLine);
                            }
                        }
                    }
                }
                catch
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR Skipping not compatible mod: " + files[i]);
                        Console.Out.Flush();
                    }
                    else
                    {
                        Console.WriteLine("Mod is not compatible: " + files[i] + Environment.NewLine);
                    }
                }
            }

            Console.WriteLine("Process textures finished.\n");

            Console.WriteLine("Saving packages started...");
            if (ipc)
            {
                Console.WriteLine("[IPC]STAGE_CONTEXT STAGE_SAVING");
                Console.Out.Flush();
            }
            cachePackageMgr.CloseAllWithSave(repack, ipc);
            Console.WriteLine("Saving packages finished.\n");

            return status;
        }

        static public string replaceTextureSpecialME3Mod(Image image, List<MatchedTexture> list, CachePackageMgr cachePackageMgr, string textureName, uint crc, string tfcName, byte[] guid)
        {
            Texture arcTexture = null, cprTexture = null;
            string errors = "";

            for (int n = 0; n < list.Count; n++)
            {
                MatchedTexture nodeTexture = list[n];
                if (nodeTexture.path == "")
                    continue;
                Package package = cachePackageMgr.OpenPackage(GameData.GamePath + nodeTexture.path);
                Texture texture = new Texture(package, nodeTexture.exportID, package.getExportData(nodeTexture.exportID));
                string fmt = texture.properties.getProperty("Format").valueName;
                PixelFormat pixelFormat = Image.getEngineFormatType(fmt);

                while (texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty))
                {
                    texture.mipMapsList.Remove(texture.mipMapsList.First(s => s.storageType == Texture.StorageTypes.empty));
                }

                if (image.mipMaps[0].origWidth / image.mipMaps[0].origHeight !=
                    texture.mipMapsList[0].width / texture.mipMapsList[0].height)
                {
                    errors += "Error in texture: " + textureName + " This texture has wrong aspect ratio, skipping texture..." + Environment.NewLine;
                    break;
                }

                if (!image.checkDDSHaveAllMipmaps() ||
                    (texture.mipMapsList.Count > 1 && image.mipMaps.Count() <= 1) ||
                    image.pixelFormat != pixelFormat)
                {
                    bool dxt1HasAlpha = false;
                    byte dxt1Threshold = 128;
                    if (pixelFormat == PixelFormat.DXT1 && texture.properties.exists("CompressionSettings"))
                    {
                        if (texture.properties.exists("CompressionSettings") &&
                            texture.properties.getProperty("CompressionSettings").valueName == "TC_OneBitAlpha")
                        {
                            dxt1HasAlpha = true;
                            if (image.pixelFormat == PixelFormat.ARGB ||
                                image.pixelFormat == PixelFormat.DXT3 ||
                                image.pixelFormat == PixelFormat.DXT5)
                            {
                                errors += "Warning for texture: " + textureName + ". This texture converted from full alpha to binary alpha." + Environment.NewLine;
                            }
                        }
                    }
                    image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                }

                // remove lower mipmaps from source image which not exist in game data
                for (int t = 0; t < image.mipMaps.Count(); t++)
                {
                    if (image.mipMaps[t].origWidth <= texture.mipMapsList[0].width &&
                        image.mipMaps[t].origHeight <= texture.mipMapsList[0].height &&
                        texture.mipMapsList.Count > 1)
                    {
                        if (!texture.mipMapsList.Exists(m => m.width == image.mipMaps[t].origWidth && m.height == image.mipMaps[t].origHeight))
                        {
                            image.mipMaps.RemoveAt(t--);
                        }
                    }
                }

                bool skip = false;
                // reuse lower mipmaps from game data which not exist in source image
                for (int t = 0; t < texture.mipMapsList.Count; t++)
                {
                    if (texture.mipMapsList[t].width <= image.mipMaps[0].origWidth &&
                        texture.mipMapsList[t].height <= image.mipMaps[0].origHeight)
                    {
                        if (!image.mipMaps.Exists(m => m.origWidth == texture.mipMapsList[t].width && m.origHeight == texture.mipMapsList[t].height))
                        {
                            byte[] data = texture.getMipMapData(texture.mipMapsList[t]);
                            if (data == null)
                            {
                                errors += "Error in game data: " + nodeTexture.path + ", skipping texture..." + Environment.NewLine;
                                skip = true;
                                break;
                            }
                            MipMap mipmap = new MipMap(data, texture.mipMapsList[t].width, texture.mipMapsList[t].height, pixelFormat);
                            image.mipMaps.Add(mipmap);
                        }
                    }
                }
                if (skip)
                    continue;

                package.DisposeCache();

                bool triggerCacheArc = false, triggerCacheCpr = false;
                string archiveFile = "";
                byte[] origGuid = new byte[16];
                if (texture.properties.exists("TextureFileCacheName"))
                {
                    TFCTexture tfc = new TFCTexture
                    {
                        guid = guid,
                        name = tfcName
                    };

                    Array.Copy(texture.properties.getProperty("TFCFileGuid").valueStruct, origGuid, 16);
                    archiveFile = Path.Combine(Path.GetDirectoryName(GameData.GamePath + nodeTexture.path), tfc.name + ".tfc");
                    texture.properties.setNameValue("TextureFileCacheName", tfc.name);
                    texture.properties.setStructValue("TFCFileGuid", "Guid", tfc.guid);
                    if (!File.Exists(archiveFile))
                    {
                        using (FileStream fs = new FileStream(archiveFile, FileMode.CreateNew, FileAccess.Write))
                        {
                            fs.WriteFromBuffer(tfc.guid);
                        }
                    }
                }

                List<Texture.MipMap> mipmaps = new List<Texture.MipMap>();
                for (int m = 0; m < image.mipMaps.Count(); m++)
                {
                    Texture.MipMap mipmap = new Texture.MipMap();
                    mipmap.width = image.mipMaps[m].origWidth;
                    mipmap.height = image.mipMaps[m].origHeight;
                    if (texture.existMipmap(mipmap.width, mipmap.height))
                        mipmap.storageType = texture.getMipmap(mipmap.width, mipmap.height).storageType;
                    else
                    {
                        mipmap.storageType = texture.getTopMipmap().storageType;
                        if (texture.mipMapsList.Count() > 1)
                        {
                            if (texture.properties.exists("TextureFileCacheName"))
                            {
                                if (texture.mipMapsList.Count < 6)
                                {
                                    mipmap.storageType = Texture.StorageTypes.pccUnc;
                                    if (!texture.properties.exists("NeverStream"))
                                    {
                                        if (package.existsNameId("NeverStream"))
                                            texture.properties.addBoolValue("NeverStream", true);
                                        else
                                            goto skip;
                                    }
                                }
                                else
                                {
                                    mipmap.storageType = Texture.StorageTypes.extZlib;
                                }
                            }
                        }
                    }

                    mipmap.uncompressedSize = image.mipMaps[m].data.Length;
                    if (mipmap.storageType == Texture.StorageTypes.extZlib ||
                        mipmap.storageType == Texture.StorageTypes.extLZO)
                    {
                        if (cprTexture == null || (cprTexture != null && mipmap.storageType != cprTexture.mipMapsList[m].storageType))
                        {
                            mipmap.newData = texture.compressTexture(image.mipMaps[m].data, mipmap.storageType);
                            triggerCacheCpr = true;
                        }
                        else
                        {
                            if (cprTexture.mipMapsList[m].width != mipmap.width ||
                                cprTexture.mipMapsList[m].height != mipmap.height)
                                throw new Exception();
                            mipmap.newData = cprTexture.mipMapsList[m].newData;
                        }
                        mipmap.compressedSize = mipmap.newData.Length;
                    }
                    if (mipmap.storageType == Texture.StorageTypes.pccUnc ||
                        mipmap.storageType == Texture.StorageTypes.extUnc)
                    {
                        mipmap.compressedSize = mipmap.uncompressedSize;
                        mipmap.newData = image.mipMaps[m].data;
                    }
                    if (mipmap.storageType == Texture.StorageTypes.extZlib ||
                        mipmap.storageType == Texture.StorageTypes.extUnc)
                    {
                        if (arcTexture == null ||
                            !StructuralComparisons.StructuralEqualityComparer.Equals(
                            arcTexture.properties.getProperty("TFCFileGuid").valueStruct,
                            texture.properties.getProperty("TFCFileGuid").valueStruct))
                        {
                            triggerCacheArc = true;
                            Texture.MipMap oldMipmap = texture.getMipmap(mipmap.width, mipmap.height);
                            if (StructuralComparisons.StructuralEqualityComparer.Equals(origGuid,
                                texture.properties.getProperty("TFCFileGuid").valueStruct) &&
                                oldMipmap.width != 0 && mipmap.newData.Length <= oldMipmap.compressedSize)
                            {
                                try
                                {
                                    using (FileStream fs = new FileStream(archiveFile, FileMode.Open, FileAccess.Write))
                                    {
                                        fs.JumpTo(oldMipmap.dataOffset);
                                        mipmap.dataOffset = oldMipmap.dataOffset;
                                        fs.WriteFromBuffer(mipmap.newData);
                                    }
                                }
                                catch
                                {
                                    throw new Exception("Problem with access to TFC file: " + archiveFile);
                                }
                            }
                            else
                            {
                                try
                                {
                                    using (FileStream fs = new FileStream(archiveFile, FileMode.Open, FileAccess.Write))
                                    {
                                        fs.SeekEnd();
                                        mipmap.dataOffset = (uint)fs.Position;
                                        fs.WriteFromBuffer(mipmap.newData);
                                    }
                                }
                                catch
                                {
                                    throw new Exception("Problem with access to TFC file: " + archiveFile);
                                }
                            }
                        }
                        else
                        {
                            if (arcTexture.mipMapsList[m].width != mipmap.width ||
                                arcTexture.mipMapsList[m].height != mipmap.height)
                                throw new Exception();
                            mipmap.dataOffset = arcTexture.mipMapsList[m].dataOffset;
                        }
                    }

                    mipmap.width = image.mipMaps[m].width;
                    mipmap.height = image.mipMaps[m].height;
                    mipmaps.Add(mipmap);
                    if (texture.mipMapsList.Count() == 1)
                        break;
                }
                texture.replaceMipMaps(mipmaps);
                texture.properties.setIntValue("SizeX", texture.mipMapsList.First().width);
                texture.properties.setIntValue("SizeY", texture.mipMapsList.First().height);
                if (texture.properties.exists("MipTailBaseIdx"))
                    texture.properties.setIntValue("MipTailBaseIdx", texture.mipMapsList.Count() - 1);

                using (MemoryStream newData = new MemoryStream())
                {
                    newData.WriteFromBuffer(texture.properties.toArray());
                    newData.WriteFromBuffer(texture.toArray(0)); // filled later
                    package.setExportData(nodeTexture.exportID, newData.ToArray());
                }

                using (MemoryStream newData = new MemoryStream())
                {
                    newData.WriteFromBuffer(texture.properties.toArray());
                    newData.WriteFromBuffer(texture.toArray(package.exportsTable[nodeTexture.exportID].dataOffset + (uint)newData.Position));
                    package.setExportData(nodeTexture.exportID, newData.ToArray());
                }

                if (triggerCacheCpr)
                    cprTexture = texture;
                if (triggerCacheArc)
                    arcTexture = texture;
                skip:
                package = null;
            }
            arcTexture = cprTexture = null;

            return errors;
        }

        static public bool extractAllTextures(MeType gameId, string outputDir, bool png, string textureTfcFilter)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            gameData.getPackages();
            if (gameId != MeType.ME1_TYPE)
                gameData.getTfcTextures();

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Program.MAINEXENAME);
            string mapFile = Path.Combine(path, "me" + (int)gameId + "map.bin");
            if (!loadTexturesMapFile(mapFile, false))
                return false;

            Console.WriteLine("Extracting textures started...");

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            for (int i = 0; i < textures.Count; i++)
            {
                if (png)
                {
                    new MipMaps().extractTextureToPng(Path.Combine(outputDir, textures[i].name +
                        string.Format("_0x{0:X8}", textures[i].crc) + ".png"), GameData.GamePath +
                        textures[i].list.Find(s => s.path != "").path, textures[i].list[0].exportID);
                }
                else
                {
                    string outputFile = Path.Combine(outputDir, textures[i].name +
                        string.Format("_0x{0:X8}", textures[i].crc) + ".dds");
                    string packagePath = GameData.GamePath + textures[i].list.Find(s => s.path != "").path;
                    int exportID = textures[i].list.Find(s => s.path != "").exportID;
                    Package package = new Package(packagePath);
                    Texture texture = new Texture(package, exportID, package.getExportData(exportID));
                    package.Dispose();
                    if (textureTfcFilter != "" && texture.properties.exists("TextureFileCacheName"))
                    {
                        string archive = texture.properties.getProperty("TextureFileCacheName").valueName;
                        if (archive != textureTfcFilter)
                            continue;
                    }
                    while (texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty))
                    {
                        texture.mipMapsList.Remove(texture.mipMapsList.First(s => s.storageType == Texture.StorageTypes.empty));
                    }
                    List<MipMap> mipmaps = new List<MipMap>();
                    PixelFormat pixelFormat = Image.getEngineFormatType(texture.properties.getProperty("Format").valueName);
                    for (int k = 0; k < texture.mipMapsList.Count; k++)
                    {
                        byte[] data = texture.getMipMapDataByIndex(k);
                        if (data == null)
                        {
                            continue;
                        }
                        mipmaps.Add(new MipMap(data, texture.mipMapsList[k].width, texture.mipMapsList[k].height, pixelFormat));
                    }
                    Image image = new Image(mipmaps, pixelFormat);
                    if (File.Exists(outputFile))
                        File.Delete(outputFile);
                    using (FileStream fs = new FileStream(outputFile, FileMode.CreateNew, FileAccess.Write))
                    {
                        image.StoreImageToDDS(fs);
                    }
                }
            }

            Console.WriteLine("Extracting textures completed.");
            return true;
        }

        public static bool CheckTextures(MeType gameId, bool ipc)
        {
            ConfIni configIni = new ConfIni();
            GameData gameData = new GameData(gameId, configIni);
            if (GameData.GamePath == null || !Directory.Exists(GameData.GamePath))
            {
                Console.WriteLine("Error: Could not found the game!");
                return false;
            }

            gameData.getPackages();
            GameData.packageFiles.Sort();
            if (gameId != MeType.ME1_TYPE)
                gameData.getTfcTextures();

            Console.WriteLine("Starting checking textures...");

            int lastProgress = -1;
            for (int i = 0; i < GameData.packageFiles.Count; i++)
            {
                Package package;
                if (ipc)
                {
                    Console.WriteLine("[IPC]PROCESSING_FILE " + GameData.packageFiles[i]);
                }
                else
                {
                    Console.WriteLine("Package " + (i + 1) + " of " + GameData.packageFiles.Count + " - " + GameData.RelativeGameData(GameData.packageFiles[i]));
                }
                int newProgress = i * 100 / GameData.packageFiles.Count;
                if (ipc && lastProgress != newProgress)
                {
                    Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                    Console.Out.Flush();
                    lastProgress = newProgress;
                }
                try
                {
                    package = new Package(GameData.packageFiles[i], true);
                }
                catch (Exception e)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR_TEXTURE_SCAN_DIAGNOSTIC Error opening package file: " + GameData.RelativeGameData(GameData.packageFiles[i]));
                        Console.Out.Flush();
                    }
                    else
                    {
                        string err = "";
                        err += "---- Start --------------------------------------------" + Environment.NewLine;
                        err += "Error opening package file: " + GameData.packageFiles[i] + Environment.NewLine;
                        err += e.Message + Environment.NewLine + Environment.NewLine;
                        err += e.StackTrace + Environment.NewLine + Environment.NewLine;
                        err += "---- End ----------------------------------------------" + Environment.NewLine + Environment.NewLine;
                        Console.WriteLine(err);
                    }
                    continue;
                }

                for (int e = 0; e < package.exportsTable.Count; e++)
                {
                    int id = package.getClassNameId(package.exportsTable[e].classId);
                    if (id == package.nameIdTexture2D ||
                        id == package.nameIdLightMapTexture2D ||
                        id == package.nameIdShadowMapTexture2D ||
                        id == package.nameIdTextureFlipBook)
                    {
                        Texture texture = null;
                        try
                        {
                            texture = new Texture(package, e, package.getExportData(e));
                        }
                        catch
                        {
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]ERROR_TEXTURE_SCAN_DIAGNOSTIC Error reading texture data for texture: " +
                                    package.exportsTable[e].objectName + " in package: " + GameData.RelativeGameData(GameData.packageFiles[i]));
                                Console.Out.Flush();
                            }
                            else
                            {
                                Console.WriteLine("Error: Failed reading texture data for texture: " +
                                    package.exportsTable[e].objectName + " in package: " + GameData.RelativeGameData(GameData.packageFiles[i]));
                            }
                            continue;
                        }
                        if (!texture.hasImageData())
                            continue;

                        for (int m = 0; m < texture.mipMapsList.Count; m++)
                        {
                            try
                            {
                                texture.getMipMapDataByIndex(m);
                            }
                            catch
                            {
                                if (ipc)
                                {
                                    Console.WriteLine("[IPC]ERROR_TEXTURE_SCAN_DIAGNOSTIC Issue opening texture data: " +
                                        package.exportsTable[i].objectName + "mipmap: " + m + " in package: " + GameData.RelativeGameData(GameData.packageFiles[i]));
                                    Console.Out.Flush();
                                }
                                else
                                {
                                    Console.WriteLine("Error: Issue opening texture data: " +
                                        package.exportsTable[i].objectName + "mipmap: " + m + " in package: " + GameData.RelativeGameData(GameData.packageFiles[i]));
                                }
                                continue;
                            }
                        }
                        texture.Dispose();
                    }
                }

                package.Dispose();
            }
            Console.WriteLine("Finished checking textures.");

            return true;
        }
    }
}
