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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MassEffectModder
{
    public partial class MipMaps
    {
        TFCTexture[] guids = new TFCTexture[]
        {
            new TFCTexture
            {
                guid = new byte[] { 0x11, 0xD3, 0xC3, 0x39, 0xB3, 0x40, 0x44, 0x61, 0xBB, 0x0E, 0x76, 0x75, 0x2D, 0xF7, 0xC3, 0xB1 },
                name = "Texture2D"
            },
            new TFCTexture
            {
                guid = new byte[] { 0x2B, 0x7D, 0x2F, 0x16, 0x63, 0x52, 0x4F, 0x3E, 0x97, 0x5B, 0x0E, 0xF2, 0xC1, 0xEB, 0xC6, 0x5D },
                name = "Format"
            },
        };

        public string replaceTexture(Image image, List<MatchedTexture> list, CachePackageMgr cachePackageMgr,
            string textureName, uint crc, bool verify)
        {
            var masterTextures = new Dictionary<Texture, int>();
            Texture arcTexture = null, cprTexture = null;
            List<byte[]> cacheCprMipmaps = null;
            string errors = "";

            for (int n = 0; n < list.Count; n++)
            {
                MatchedTexture nodeTexture = list[n];
                if (nodeTexture.path == "")
                    continue;
                Package package;
                try
                {
                    package = cachePackageMgr.OpenPackage(GameData.GamePath + nodeTexture.path);
                }
                catch (Exception e)
                {
                    errors += "---- Start --------------------------------------------" + Environment.NewLine;
                    errors += "Error opening package file: " + GameData.GamePath + nodeTexture.path + Environment.NewLine;
                    errors += e.Message + Environment.NewLine + Environment.NewLine;
                    errors += e.StackTrace + Environment.NewLine + Environment.NewLine;
                    errors += "---- End ----------------------------------------------" + Environment.NewLine + Environment.NewLine;
                    Console.WriteLine(errors);
                    break;
                }
                Texture texture = new Texture(package, nodeTexture.exportID, package.getExportData(nodeTexture.exportID));
                string fmt = texture.properties.getProperty("Format").valueName;
                PixelFormat pixelFormat = Image.getPixelFormatType(fmt);

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

                if (GameData.gameType == MeType.ME1_TYPE && texture.mipMapsList.Count < 6)
                {
                    for (int i = texture.mipMapsList.Count - 1; i != 0; i--)
                        texture.mipMapsList.RemoveAt(i);
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
                    if ((pixelFormat == PixelFormat.DXT5 || pixelFormat == PixelFormat.DXT1 || pixelFormat == PixelFormat.ATI2) &&
                         (image.pixelFormat == PixelFormat.RGB || image.pixelFormat == PixelFormat.ARGB))
                    {
                        if (image.pixelFormat == PixelFormat.RGB && pixelFormat == PixelFormat.DXT5)
                        {
                            errors += "Warning for texture: " + textureName + ". This texture converted from full alpha to no alpha.";
                        }
                        pixelFormat = PixelFormat.ARGB;
                    }
                    image.correctMips(pixelFormat, dxt1HasAlpha, dxt1Threshold);
                }

                /*fmt = Image.getEngineFormatType(pixelFormat);
                if (!package.existsNameId(fmt))
                    package.addName(fmt);
                string cmp = "TC_NormalmapUncompressed";
                if (!package.existsNameId(cmp))
                    package.addName(cmp);
                if (!package.existsNameId("CompressionSettings"))
                    package.addName("CompressionSettings");
                if (GameData.gameType == MeType.ME3_TYPE)
                {
                    texture.properties.setByteValue("Format", fmt, "EPixelFormat");
                    //texture.properties.setByteValue("CompressionSettings", cmp, "TextureCompressionSettings");
                }
                else
                {
                    texture.properties.setByteValue("Format", fmt, "");
                    //texture.properties.setByteValue("CompressionSettings", cmp, "");
                }*/

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

                texture.properties.removeProperty("LODGroup");
                if (!package.existsNameId("LODGroup"))
                    package.addName("LODGroup");
                if (GameData.gameType == MeType.ME3_TYPE)
                {
                    if (!package.existsNameId("TextureGroup"))
                        package.addName("TextureGroup");
                    if (!package.existsNameId("TEXTUREGROUP_ShadowMap"))
                        package.addName("TEXTUREGROUP_ShadowMap");
                    texture.properties.addByteValue("LODGroup", "TEXTUREGROUP_ShadowMap", "TextureGroup", 0);
                }
                else
                {
                    if (!package.existsNameId("TEXTUREGROUP_LightAndShadowMap"))
                        package.addName("TEXTUREGROUP_LightAndShadowMap");
                    texture.properties.addByteValue("LODGroup", "TEXTUREGROUP_LightAndShadowMap", "", 0);
                }

                if (cacheCprMipmaps == null)
                {
                    cacheCprMipmaps = new List<byte[]>();
                    for (int m = 0; m < image.mipMaps.Count(); m++)
                    {
                        if (GameData.gameType == MeType.ME1_TYPE)
                            cacheCprMipmaps.Add(texture.compressTexture(image.mipMaps[m].data, Texture.StorageTypes.extLZO));
                        else
                            cacheCprMipmaps.Add(texture.compressTexture(image.mipMaps[m].data, Texture.StorageTypes.extZlib));
                    }
                }

                bool triggerCacheArc = false, triggerCacheCpr = false;
                string archiveFile = "";
                byte[] origGuid = new byte[16];
                if (texture.properties.exists("TextureFileCacheName"))
                {
                    Array.Copy(texture.properties.getProperty("TFCFileGuid").valueStruct, origGuid, 16);
                    string archive = texture.properties.getProperty("TextureFileCacheName").valueName;
                    archiveFile = Path.Combine(GameData.MainData, archive + ".tfc");
                    if (nodeTexture.path.ToLowerInvariant().Contains("\\dlc"))
                    {
                        string DLCArchiveFile = Path.Combine(Path.GetDirectoryName(GameData.GamePath + nodeTexture.path), archive + ".tfc");
                        if (File.Exists(DLCArchiveFile))
                            archiveFile = DLCArchiveFile;
                        else if (!File.Exists(archiveFile))
                        {
                            List<string> files = Directory.GetFiles(GameData.bioGamePath, archive + ".tfc",
                                SearchOption.AllDirectories).Where(item => item.EndsWith(".tfc", StringComparison.OrdinalIgnoreCase)).ToList();
                            if (files.Count == 1)
                                archiveFile = files[0];
                            else if (files.Count == 0)
                            {
                                DLCArchiveFile = Path.Combine(Path.GetDirectoryName(DLCArchiveFile),
                                    "Textures_" + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(GameData.GamePath + nodeTexture.path)) + ".tfc"));
                                if (File.Exists(DLCArchiveFile))
                                    archiveFile = DLCArchiveFile;
                                else
                                    archiveFile = Path.Combine(GameData.MainData, "Textures.tfc");
                            }
                            else
                                throw new Exception("");
                        }
                    }

                    // dry run to test if texture fit in old space
                    bool oldSpace = true;
                    for (int m = 0; m < image.mipMaps.Count(); m++)
                    {
                        Texture.MipMap mipmap = new Texture.MipMap();
                        mipmap.width = image.mipMaps[m].origWidth;
                        mipmap.height = image.mipMaps[m].origHeight;
                        if (texture.existMipmap(mipmap.width, mipmap.height))
                            mipmap.storageType = texture.getMipmap(mipmap.width, mipmap.height).storageType;
                        else
                        {
                            oldSpace = false;
                            break;
                        }

                        if (mipmap.storageType == Texture.StorageTypes.extZlib ||
                            mipmap.storageType == Texture.StorageTypes.extLZO)
                        {
                            Texture.MipMap oldMipmap = texture.getMipmap(mipmap.width, mipmap.height);
                            if (cacheCprMipmaps[m].Length > oldMipmap.compressedSize)
                            {
                                oldSpace = false;
                                break;
                            }
                        }
                        if (texture.mipMapsList.Count() == 1)
                            break;
                    }

                    long fileLength = new FileInfo(archiveFile).Length;
                    if (!oldSpace && fileLength + 0x5000000 > 0x80000000)
                    {
                        archiveFile = "";
                        foreach (TFCTexture newGuid in guids)
                        {
                            archiveFile = Path.Combine(GameData.MainData, newGuid.name + ".tfc");
                            if (!File.Exists(archiveFile))
                            {
                                texture.properties.setNameValue("TextureFileCacheName", newGuid.name);
                                texture.properties.setStructValue("TFCFileGuid", "Guid", newGuid.guid);
                                using (FileStream fs = new FileStream(archiveFile, FileMode.CreateNew, FileAccess.Write))
                                {
                                    fs.WriteFromBuffer(newGuid.guid);
                                }
                                break;
                            }
                            else
                            {
                                fileLength = new FileInfo(archiveFile).Length;
                                if (fileLength + 0x5000000 < 0x80000000)
                                {
                                    texture.properties.setNameValue("TextureFileCacheName", newGuid.name);
                                    texture.properties.setStructValue("TFCFileGuid", "Guid", newGuid.guid);
                                    break;
                                }
                            }
                            archiveFile = "";
                        }
                        if (archiveFile == "")
                            throw new Exception("No free TFC texture file!");
                    }
                }

                if (verify)
                    nodeTexture.crcs = new List<uint>();
                List<Texture.MipMap> mipmaps = new List<Texture.MipMap>();
                for (int m = 0; m < image.mipMaps.Count(); m++)
                {
                    if (verify)
                        nodeTexture.crcs.Add(texture.getCrcData(image.mipMaps[m].data));
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
                            // WA: remove later someday
                            if (GameData.gameType == MeType.ME1_TYPE && texture.properties.exists("NeverStream"))
                            {
                                nodeTexture.linkToMaster = -1;
                            }

                            if (GameData.gameType == MeType.ME1_TYPE && nodeTexture.linkToMaster == -1)
                            {
                                if (mipmap.storageType == Texture.StorageTypes.pccUnc)
                                {
                                    mipmap.storageType = Texture.StorageTypes.pccLZO;
                                }
                            }
                            else if (GameData.gameType == MeType.ME1_TYPE && nodeTexture.linkToMaster != -1)
                            {
                                if (mipmap.storageType == Texture.StorageTypes.pccUnc ||
                                    mipmap.storageType == Texture.StorageTypes.pccLZO ||
                                    mipmap.storageType == Texture.StorageTypes.pccZlib)
                                {
                                    mipmap.storageType = Texture.StorageTypes.extLZO;
                                }
                            }
                            else if (GameData.gameType == MeType.ME2_TYPE || GameData.gameType == MeType.ME3_TYPE)
                            {
                                if (texture.properties.exists("TextureFileCacheName"))
                                {
                                    if (texture.mipMapsList.Count < 6)
                                    {
                                        mipmap.storageType = Texture.StorageTypes.pccUnc;
                                        if (!texture.properties.exists("NeverStream"))
                                        {
                                            if (!package.existsNameId("NeverStream"))
                                                package.addName("NeverStream");
                                            texture.properties.addBoolValue("NeverStream", true);
                                        }
                                    }
                                    else
                                    {
                                        if (GameData.gameType == MeType.ME2_TYPE)
                                            mipmap.storageType = Texture.StorageTypes.extLZO;
                                        else
                                            mipmap.storageType = Texture.StorageTypes.extZlib;
                                    }
                                }
                            }
                        }
                    }

                    if (GameData.gameType == MeType.ME1_TYPE)
                    {
                        // WA Force back to LZO compression
                        if (mipmap.storageType == Texture.StorageTypes.extZlib)
                            mipmap.storageType = Texture.StorageTypes.extLZO;
                        if (mipmap.storageType == Texture.StorageTypes.pccZlib)
                            mipmap.storageType = Texture.StorageTypes.pccLZO;
                    }
                    else
                    {
                        if (mipmap.storageType == Texture.StorageTypes.extLZO)
                            mipmap.storageType = Texture.StorageTypes.extZlib;
                        if (mipmap.storageType == Texture.StorageTypes.pccLZO)
                            mipmap.storageType = Texture.StorageTypes.pccZlib;
                    }

                    mipmap.uncompressedSize = image.mipMaps[m].data.Length;
                    if (GameData.gameType == MeType.ME1_TYPE)
                    {
                        if (mipmap.storageType == Texture.StorageTypes.pccLZO ||
                            mipmap.storageType == Texture.StorageTypes.pccZlib)
                        {
                            if (nodeTexture.linkToMaster == -1)
                                mipmap.newData = cacheCprMipmaps[m];
                            else
                                mipmap.newData = masterTextures.First(s => s.Value == nodeTexture.linkToMaster).Key.mipMapsList[m].newData;
                            mipmap.compressedSize = mipmap.newData.Length;
                        }
                        if (mipmap.storageType == Texture.StorageTypes.pccUnc)
                        {
                            mipmap.compressedSize = mipmap.uncompressedSize;
                            mipmap.newData = image.mipMaps[m].data;
                        }
                        if ((mipmap.storageType == Texture.StorageTypes.extLZO ||
                            mipmap.storageType == Texture.StorageTypes.extZlib) && nodeTexture.linkToMaster != -1)
                        {
                            mipmap.compressedSize = masterTextures.First(s => s.Value == nodeTexture.linkToMaster).Key.mipMapsList[m].compressedSize;
                            mipmap.dataOffset = masterTextures.First(s => s.Value == nodeTexture.linkToMaster).Key.mipMapsList[m].dataOffset;
                        }
                    }
                    else
                    {
                        if (mipmap.storageType == Texture.StorageTypes.extZlib ||
                            mipmap.storageType == Texture.StorageTypes.extLZO)
                        {
                            if (cprTexture == null || (cprTexture != null && mipmap.storageType != cprTexture.mipMapsList[m].storageType))
                            {
                                mipmap.newData = cacheCprMipmaps[m];
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
                            mipmap.storageType == Texture.StorageTypes.extLZO ||
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
                    }

                    mipmap.width = image.mipMaps[m].width;
                    mipmap.height = image.mipMaps[m].height;
                    mipmaps.Add(mipmap);
                    if (texture.mipMapsList.Count() == 1)
                        break;
                }

                if (verify)
                    list[n] = nodeTexture;

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

                if (GameData.gameType == MeType.ME1_TYPE)
                {
                    if (nodeTexture.linkToMaster == -1)
                        masterTextures.Add(texture, n);
                }
                else
                {
                    if (triggerCacheCpr)
                        cprTexture = texture;
                    if (triggerCacheArc)
                        arcTexture = texture;
                }
                package = null;
            }
            masterTextures = null;
            arcTexture = cprTexture = null;

            return errors;
        }
    }
}
