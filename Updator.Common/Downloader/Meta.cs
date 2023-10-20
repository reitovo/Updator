﻿using System.Collections.Immutable;
namespace Updator.Common.Downloader;

public class Meta {
   public const int WinVersion = 62;
   public const int MacVersion = 62;
   public const int LinuxVersion = 62;

   public static int RuntimeVersion {
      get {
         if (OperatingSystem.IsMacOS())
            return MacVersion;
         if (OperatingSystem.IsLinux())
            return LinuxVersion;
         return WinVersion;
      }
   }

   public static ImmutableDictionary<string, int> VersionByRuntime { get; } = new Dictionary<string, int>() {
      { "win", WinVersion },
      { "osx", MacVersion },
      { "linux", LinuxVersion }
   }.ToImmutableDictionary();

   public static string RuntimeString {
      get {
         if (OperatingSystem.IsMacOS())
            return "osx";
         if (OperatingSystem.IsLinux())
            return "linux";
         return "win";
      }
   }
}
