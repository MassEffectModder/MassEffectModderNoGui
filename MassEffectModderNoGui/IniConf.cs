/*
 * MassEffectModder
 *
 * Copyright (C) 2014-2017 Pawel Kolodziejski
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

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MassEffectModder
{
    public class ConfIni
    {
        string _iniPath;

        [DllImport("kernel32")]
        private static extern uint GetPrivateProfileString(string section, string key,
                string def, StringBuilder value, int size, string filename);

        [DllImport("kernel32")]
        private static extern bool WritePrivateProfileString(string section, string key,
                string value, string filename);

        public ConfIni(string iniPath = null)
        {
            if (iniPath != null)
                _iniPath = iniPath;
            else
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Program.MAINEXENAME);
                _iniPath = Path.Combine(path, Program.MAINEXENAME + ".ini");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
        }

        public string Read(string key, string section)
        {
            if (_iniPath == null || key == null)
                throw new Exception();

            StringBuilder str = new StringBuilder(256);
            GetPrivateProfileString(section, key, "", str, str.MaxCapacity, _iniPath);
            return str.ToString();
        }

        public bool Write(string key, string value, string section)
        {
            if (_iniPath == null || key == null || value == null || section == null)
                throw new Exception();

            return WritePrivateProfileString(section, key, value, _iniPath);
        }

        public bool DeleteKey(string key, string section)
        {
            if (_iniPath == null || key == null || section == null)
                throw new Exception();

            return WritePrivateProfileString(section, key, null, _iniPath);
        }

        public bool DeleteSection(string section)
        {
            if (_iniPath == null || section == null)
                throw new Exception();

            return WritePrivateProfileString(section, null, null, _iniPath);
        }
    }
}
