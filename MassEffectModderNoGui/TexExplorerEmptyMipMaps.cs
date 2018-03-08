/*
 * MassEffectModder
 *
 * Copyright (C) 2014-2018 Pawel Kolodziejski <aquadran at users.sourceforge.net>
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

using StreamHelpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MassEffectModder
{
    public partial class MipMaps
    {
        public struct RemoveMipsEntry
        {
            public string pkgPath;
            public List<int> exportIDs;
        }

        public void AddMarkerToPackages(string packagePath)
        {
            string path = "";
            if (GameData.gameType == MeType.ME1_TYPE)
            {
                path = @"\BioGame\CookedPC\testVolumeLight_VFX.upk".ToLowerInvariant();
            }
            if (GameData.gameType == MeType.ME2_TYPE)
            {
                path = @"\BioGame\CookedPC\BIOC_Materials.pcc".ToLowerInvariant();
            }
            if (path != "" && packagePath.ToLowerInvariant().Contains(path))
                return;
            try
            {
                using (FileStream fs = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    fs.SeekEnd();
                    fs.Seek(-Package.MEMendFileMarker.Length, SeekOrigin.Current);
                    string marker = fs.ReadStringASCII(Package.MEMendFileMarker.Length);
                    if (marker != Package.MEMendFileMarker)
                    {
                        fs.SeekEnd();
                        fs.WriteStringASCII(Package.MEMendFileMarker);
                    }
                }
            }
            catch
            {
            }
        }

        public List<RemoveMipsEntry> prepareListToRemove(List<FoundTexture> textures)
        {
            List<RemoveMipsEntry> list = new List<RemoveMipsEntry>();

            for (int k = 0; k < textures.Count; k++)
            {
                for (int t = 0; t < textures[k].list.Count; t++)
                {
                    if (textures[k].list[t].removeEmptyMips)
                    {
                        bool found = false;
                        for (int e = 0; e < list.Count; e++)
                        {
                            if (list[e].pkgPath == textures[k].list[t].path)
                            {
                                list[e].exportIDs.Add(textures[k].list[t].exportID);
                                found = true;
                                break;
                            }
                        }
                        if (found)
                            continue;
                        RemoveMipsEntry entry = new RemoveMipsEntry();
                        entry.pkgPath = textures[k].list[t].path;
                        entry.exportIDs = new List<int>();
                        entry.exportIDs.Add(textures[k].list[t].exportID);
                        list.Add(entry);
                    }
                }
            }

            return list;
        }

        public void removeMipMapsME1(int phase, List<FoundTexture> textures, bool ipc)
        {
            int lastProgress = -1;
            List<RemoveMipsEntry> list = prepareListToRemove(textures);
            int current = -1;
            string path = GameData.GamePath + @"\BioGame\CookedPC\testVolumeLight_VFX.upk";
            for (int i = 0; i < GameData.packageFiles.Count; i++)
            {
                if (path == GameData.packageFiles[i])
                    continue;
                if (!list.Exists(s => (GameData.GamePath + s.pkgPath) == GameData.packageFiles[i]))
                {
                    AddMarkerToPackages(GameData.packageFiles[i]);
                    continue;
                }
                current++;
                bool modified = false;
                Package package = null;
                int newProgress = (list.Count * (phase - 1) + current + 1) * 100 / (list.Count * 2);
                if (ipc && lastProgress != newProgress)
                {
                    Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                    Console.Out.Flush();
                    lastProgress = newProgress;
                }

                try
                {
                    package = new Package(GameData.GamePath + list[current].pkgPath, true);
                }
                catch (Exception e)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR Issue opening package file: " + list[current].pkgPath);
                        Console.Out.Flush();
                    }
                    else
                    {
                        string err = "";
                        err += "---- Start --------------------------------------------" + Environment.NewLine;
                        err += "Issue opening package file: " + list[current].pkgPath + Environment.NewLine;
                        err += e.Message + Environment.NewLine + Environment.NewLine;
                        err += e.StackTrace + Environment.NewLine + Environment.NewLine;
                        err += "---- End ----------------------------------------------" + Environment.NewLine + Environment.NewLine;
                        Console.WriteLine(err);
                    }
                    continue;
                }

                for (int l = 0; l < list[current].exportIDs.Count; l++)
                {
                    int exportID = list[current].exportIDs[l];
                    using (Texture texture = new Texture(package, exportID, package.getExportData(exportID), false))
                    {
                        if (!texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty))
                        {
                            continue;
                        }
                        do
                        {
                            texture.mipMapsList.Remove(texture.mipMapsList.First(s => s.storageType == Texture.StorageTypes.empty));
                        } while (texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty));
                        texture.properties.setIntValue("SizeX", texture.mipMapsList.First().width);
                        texture.properties.setIntValue("SizeY", texture.mipMapsList.First().height);
                        texture.properties.setIntValue("MipTailBaseIdx", texture.mipMapsList.Count() - 1);

                        FoundTexture foundTexture = new FoundTexture();
                        int foundListEntry = -1;
                        string pkgName = GameData.RelativeGameData(package.packagePath).ToLowerInvariant();
                        for (int k = 0; k < textures.Count; k++)
                        {
                            for (int t = 0; t < textures[k].list.Count; t++)
                            {
                                if (textures[k].list[t].exportID == exportID &&
                                    textures[k].list[t].path.ToLowerInvariant() == pkgName)
                                {
                                    foundTexture = textures[k];
                                    foundListEntry = t;
                                    break;
                                }
                            }
                        }
                        if (foundListEntry == -1)
                        {
                            if (ipc)
                            {
                                Console.WriteLine("[IPC]ERROR Texture " + package.exportsTable[exportID].objectName + " not found in package: " + list[current].pkgPath + ", skipping...");
                                Console.Out.Flush();
                            }
                            else
                            {
                                Console.WriteLine("Error: Texture " + package.exportsTable[exportID].objectName + " not found in package: " + list[current].pkgPath + ", skipping..." + Environment.NewLine);
                            }
                            goto skip;
                        }

                        if (foundTexture.list[foundListEntry].linkToMaster != -1)
                        {
                            if (phase == 1)
                                continue;

                            MatchedTexture foundMasterTex = foundTexture.list[foundTexture.list[foundListEntry].linkToMaster];
                            Package masterPkg = null;
                            masterPkg = new Package(GameData.GamePath + foundMasterTex.path);
                            int masterExportId = foundMasterTex.exportID;
                            byte[] masterData = masterPkg.getExportData(masterExportId);
                            masterPkg.DisposeCache();
                            using (Texture masterTexture = new Texture(masterPkg, masterExportId, masterData, false))
                            {
                                if (texture.mipMapsList.Count != masterTexture.mipMapsList.Count)
                                {
                                    if (ipc)
                                    {
                                        Console.WriteLine("[IPC]ERROR Texture " + package.exportsTable[exportID].objectName + " in package: " + foundMasterTex.path + " has wrong reference, skipping..." + Environment.NewLine);
                                        Console.Out.Flush();
                                    }
                                    else
                                    {
                                        Console.WriteLine("Error: Texture " + package.exportsTable[exportID].objectName + " in package: " + foundMasterTex.path + " has wrong reference, skipping..." + Environment.NewLine);
                                    }
                                    goto skip;
                                }
                                for (int t = 0; t < texture.mipMapsList.Count; t++)
                                {
                                    Texture.MipMap mipmap = texture.mipMapsList[t];
                                    if (mipmap.storageType == Texture.StorageTypes.extLZO ||
                                        mipmap.storageType == Texture.StorageTypes.extZlib ||
                                        mipmap.storageType == Texture.StorageTypes.extUnc)
                                    {
                                        mipmap.dataOffset = masterPkg.exportsTable[masterExportId].dataOffset + (uint)masterTexture.properties.propertyEndOffset + masterTexture.mipMapsList[t].internalOffset;
                                        texture.mipMapsList[t] = mipmap;
                                    }
                                }
                            }
                            masterPkg.Dispose();
                        }
skip:
                        using (MemoryStream newData = new MemoryStream())
                        {
                            newData.WriteFromBuffer(texture.properties.toArray());
                            newData.WriteFromBuffer(texture.toArray(package.exportsTable[exportID].dataOffset + (uint)newData.Position));
                            package.setExportData(exportID, newData.ToArray());
                        }
                        modified = true;
                    }
                }
                if (modified)
                {
                    package.SaveToFile();
                }
                else
                {
                    package.Dispose();
                    AddMarkerToPackages(GameData.GamePath + list[current].pkgPath);
                }
                package.Dispose();
            }
        }

        public void removeMipMapsME2ME3(List<FoundTexture> textures, bool ipc, bool forceZlib = false)
        {
            int lastProgress = -1;
            List<RemoveMipsEntry> list = prepareListToRemove(textures);
            int current = -1;
            string path = "";
            if (GameData.gameType == MeType.ME2_TYPE)
            {
                path = GameData.GamePath + @"\BioGame\CookedPC\BIOC_Materials.pcc";
            }
            for (int i = 0; i < GameData.packageFiles.Count; i++)
            {
                if (path != "" && path == GameData.packageFiles[i])
                    continue;
                if (!list.Exists(s => (GameData.GamePath + s.pkgPath) == GameData.packageFiles[i]))
                {
                    AddMarkerToPackages(GameData.packageFiles[i]);
                    continue;
                }
                current++;
                bool modified = false;
                Package package = null;
                int newProgress = current * 100 / list.Count;
                if (ipc && lastProgress != newProgress)
                {
                    Console.WriteLine("[IPC]TASK_PROGRESS " + newProgress);
                    Console.Out.Flush();
                    lastProgress = newProgress;
                }

                try
                {
                    package = new Package(GameData.GamePath + list[current].pkgPath, true);
                }
                catch (Exception e)
                {
                    if (ipc)
                    {
                        Console.WriteLine("[IPC]ERROR Issue opening package file: " + list[current].pkgPath);
                        Console.Out.Flush();
                    }
                    else
                    {
                        string err = "";
                        err += "---- Start --------------------------------------------" + Environment.NewLine;
                        err += "Issue opening package file: " + list[current].pkgPath + Environment.NewLine;
                        err += e.Message + Environment.NewLine + Environment.NewLine;
                        err += e.StackTrace + Environment.NewLine + Environment.NewLine;
                        err += "---- End ----------------------------------------------" + Environment.NewLine + Environment.NewLine;
                        Console.WriteLine(err);
                    }
                    continue;
                }

                for (int l = 0; l < list[current].exportIDs.Count; l++)
                {
                    int exportID = list[current].exportIDs[l];
                    using (Texture texture = new Texture(package, exportID, package.getExportData(exportID), false))
                    {
                        if (!texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty))
                        {
                            continue;
                        }
                        do
                        {
                            texture.mipMapsList.Remove(texture.mipMapsList.First(s => s.storageType == Texture.StorageTypes.empty));
                        } while (texture.mipMapsList.Exists(s => s.storageType == Texture.StorageTypes.empty));
                        texture.properties.setIntValue("SizeX", texture.mipMapsList.First().width);
                        texture.properties.setIntValue("SizeY", texture.mipMapsList.First().height);
                        texture.properties.setIntValue("MipTailBaseIdx", texture.mipMapsList.Count() - 1);

                        using (MemoryStream newData = new MemoryStream())
                        {
                            newData.WriteFromBuffer(texture.properties.toArray());
                            newData.WriteFromBuffer(texture.toArray(package.exportsTable[exportID].dataOffset + (uint)newData.Position));
                            package.setExportData(exportID, newData.ToArray());
                        }
                        modified = true;
                    }
                }
                if (modified)
                {
                    if (package.compressed && package.compressionType != Package.CompressionType.Zlib)
                        package.SaveToFile(forceZlib);
                    else
                        package.SaveToFile();
                    if (forceZlib && CmdLineTools.pkgsToRepack != null)
                        CmdLineTools.pkgsToRepack.Remove(package.packagePath);
                }
                else
                {
                    package.Dispose();
                    AddMarkerToPackages(GameData.GamePath + list[current].pkgPath);
                }
                package.Dispose();
            }
            if (GameData.gameType == MeType.ME3_TYPE)
            {
                TOCBinFile.UpdateAllTOCBinFiles();
            }
        }

    }
}
