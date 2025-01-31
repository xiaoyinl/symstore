﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.FileFormats;
using Microsoft.FileFormats.ELF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Microsoft.SymbolStore.KeyGenerators
{
    public class ELFFileKeyGenerator : KeyGenerator
    {
        /// <summary>
        /// The default symbol file extension used by .NET Core.
        /// </summary>
        private const string SymbolFileExtension = ".dbg";

        private const string IdentityPrefix = "elf-buildid";
        private const string SymbolPrefix = "elf-buildid-sym";
        private const string CoreClrPrefix = "elf-buildid-coreclr";
        private const string CoreClrFileName = "libcoreclr.so";

        private static HashSet<string> s_coreClrSpecialFiles = new HashSet<string>(new string[] { "libmscordaccore.so", "libmscordbi.so", "libsos.so", "SOS.NETCore.dll" });

        private readonly ELFFile _elfFile;
        private readonly string _path;

        public ELFFileKeyGenerator(ITracer tracer, ELFFile elfFile, string path)
            : base(tracer)
        {
            _elfFile = elfFile;
            _path = path;
        }

        public ELFFileKeyGenerator(ITracer tracer, SymbolStoreFile file)
            : this(tracer, new ELFFile(new StreamAddressSpace(file.Stream)), file.FileName)
        {
        }

        public override bool IsValid()
        {
            return _elfFile.IsValid() &&
                (_elfFile.Header.Type == ELFHeaderType.Executable || _elfFile.Header.Type == ELFHeaderType.Shared);
        }

        public override IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags)
        {
            if (IsValid())
            {
                byte[] buildId = _elfFile.BuildID;
                if (buildId != null && buildId.Length == 20)
                {
                    bool symbolFile = Path.GetExtension(_path) == SymbolFileExtension;
                    string symbolFileName = GetSymbolFileName();
                    foreach (SymbolStoreKey key in GetKeys(flags, _path, buildId, symbolFile, symbolFileName))
                    {
                        yield return key;
                    }
                }
                else
                {
                    Tracer.Error("Invalid ELF BuildID '{0}' for {1}", buildId == null ? "<null>" : ToHexString(buildId), _path);
                }
            }
        }

        /// <summary>
        /// Creates the ELF file symbol store keys.
        /// </summary>
        /// <param name="flags">type of keys to return</param>
        /// <param name="path">file name and path</param>
        /// <param name="buildId">ELF file uuid bytes</param>
        /// <param name="symbolFile">if true, use the symbol file tag</param>
        /// <param name="symbolFileName">name of symbol file (from .gnu_debuglink) or null</param>
        /// <returns>symbol store keys</returns>
        public static IEnumerable<SymbolStoreKey> GetKeys(KeyTypeFlags flags, string path, byte[] buildId, bool symbolFile, string symbolFileName)
        {
            Debug.Assert(path != null);
            Debug.Assert(buildId != null && buildId.Length == 20);

            if ((flags & KeyTypeFlags.IdentityKey) != 0)
            {
                if (symbolFile)
                {
                    yield return BuildKey(path, SymbolPrefix, buildId, "_.debug");
                }
                else
                {
                    bool clrSpecialFile = s_coreClrSpecialFiles.Contains(GetFileName(path));
                    yield return BuildKey(path, IdentityPrefix, buildId, clrSpecialFile);
                }
            }
            if (!symbolFile)
            {
                if ((flags & KeyTypeFlags.SymbolKey) != 0)
                {
                    if (string.IsNullOrEmpty(symbolFileName))
                    {
                        symbolFileName = path + SymbolFileExtension;
                    }
                    yield return BuildKey(symbolFileName, SymbolPrefix, buildId, "_.debug");
                }
                if ((flags & KeyTypeFlags.ClrKeys) != 0)
                {
                    /// Creates all the special CLR keys if the path is the coreclr module for this platform
                    if (GetFileName(path) == CoreClrFileName)
                    {
                        foreach (string specialFileName in s_coreClrSpecialFiles)
                        {
                            yield return BuildKey(specialFileName, CoreClrPrefix, buildId);
                        }
                    }
                }
            }
        }

        private string GetSymbolFileName()
        {
            try
            {
                ELFSection section = _elfFile.FindSectionByName(".gnu_debuglink");
                if (section != null)
                {
                    return section.Contents.Read<string>(0);
                }
            }
            catch (Exception ex) when (ex is InvalidVirtualAddressException || ex is BadInputFormatException)
            {
                Tracer.Verbose("ELF .gnu_debuglink section in {0}: {1}", _path, ex.Message);
            }
            return null;
        }
    }
}