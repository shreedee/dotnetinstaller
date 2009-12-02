using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Vestris.ResourceLib;
using System.IO;
using CabLib;

namespace InstallerLib
{
    /// <summary>
    /// An installer linker.
    /// </summary>
    public static class InstallerLinker
    {
        public static void CreateInstaller(InstallerLinkerArguments args)
        {
            args.Validate();

            args.WriteLine(string.Format("Creating \"{0}\" from \"{1}\"", args.output, args.template));
            System.IO.File.Copy(args.template, args.output, true);
            System.IO.File.SetAttributes(args.output, System.IO.FileAttributes.Normal);

            if (!string.IsNullOrEmpty(args.banner))
            {
                args.WriteLine(string.Format("Embedding banner \"{0}\"", args.banner));
                ResourceUpdate.WriteFile(args.output, new ResourceId("CUSTOM"), new ResourceId("RES_BANNER"), 0, args.banner);
            }

            if (!string.IsNullOrEmpty(args.splash))
            {
                args.WriteLine(string.Format("Embedding splash screen \"{0}\"", args.splash));
                ResourceUpdate.WriteFile(args.output, new ResourceId("CUSTOM"), new ResourceId("RES_SPLASH"), 0, args.splash);
            }

            args.WriteLine(string.Format("Embedding configuration \"{0}\"", args.config));
            ResourceUpdate.WriteFile(args.output, new ResourceId("CUSTOM"), new ResourceId("RES_CONFIGURATION"), 0, args.config);

            ConfigFile configfile = new ConfigFile();
            configfile.Load(args.config);

            // \todo: check XML with XSD, warn if nodes are being dropped
            args.WriteLine(string.Format("Updating binary attributes in \"{0}\"", args.output));
            VersionResource rc = new VersionResource();
            rc.LoadFrom(args.output);

            // version information
            StringFileInfo stringFileInfo = (StringFileInfo)rc["StringFileInfo"];
            if (!string.IsNullOrEmpty(configfile.productversion))
            {
                rc.ProductVersion = configfile.productversion;
                stringFileInfo["ProductVersion"] = configfile.productversion;
            }

            if (!string.IsNullOrEmpty(configfile.fileversion))
                rc.FileVersion = configfile.fileversion;

            foreach (FileAttribute attr in configfile.fileattributes)
            {
                args.WriteLine(string.Format(" {0}: {1}", attr.name, attr.value));
                stringFileInfo[attr.name] = attr.value;
            }

            rc.Language = ResourceUtil.NEUTRALLANGID;
            rc.SaveTo(args.output);

            // optional icon
            if (!string.IsNullOrEmpty(args.icon))
            {
                args.WriteLine(string.Format("Embedding icon \"{0}\"", args.icon));
                IconFile iconFile = new IconFile(args.icon);
                List<string> iconSizes = new List<string>();
                foreach (IconFileIcon icon in iconFile.Icons)
                    iconSizes.Add(icon.ToString());
                args.WriteLine(string.Format(" {0}", string.Join(", ", iconSizes.ToArray())));                
                IconDirectoryResource iconDirectory = new IconDirectoryResource(iconFile);
                iconDirectory.Name = new ResourceId(128);                
                iconDirectory.Language = ResourceUtil.NEUTRALLANGID;
                iconDirectory.SaveTo(args.output);
            }

            string supportdir = string.IsNullOrEmpty(args.apppath)
                ? Environment.CurrentDirectory
                : args.apppath;

            // resource files
            ResourceFileCollection r_files = configfile.GetResources(supportdir);
            foreach (ResourceFilePair r_pair in r_files)
            {
                args.WriteLine(string.Format("Embedding resource \"{0}\": {1}", r_pair.id, r_pair.path));
                ResourceUpdate.WriteFile(args.output, new ResourceId("CUSTOM"), new ResourceId(r_pair.id), 0, r_pair.path);
            }

            // embedded files
            if (args.embed)
            {
                long totalSize = 0;
                List<String> allFilesList = new List<string>();

                args.WriteLine(string.Format("Compressing files in \"{0}\"", supportdir));
                Dictionary<string, EmbedFileCollection> all_files = configfile.GetFiles(string.Empty, supportdir);
                // ensure at least one for additional command-line parameters
                if (all_files.Count == 0) all_files.Add(string.Empty, new EmbedFileCollection(supportdir));
                Dictionary<string, EmbedFileCollection>.Enumerator enumerator = all_files.GetEnumerator();
                while(enumerator.MoveNext())
                {
                    EmbedFileCollection c_files = enumerator.Current.Value;

                    // add additional command-line files to the root CAB
                    if (string.IsNullOrEmpty(enumerator.Current.Key))
                    {
                        if (args.embedFiles != null)
                        {
                            foreach (string filename in args.embedFiles)
                            {
                                string fullpath = Path.Combine(args.apppath, filename);
                                c_files.Add(new EmbedFilePair(fullpath, filename));
                            }
                        }

                        if (args.embedFolders != null)
                        {
                            foreach (string folder in args.embedFolders)
                            {
                                c_files.AddDirectory(folder);
                            }
                        }
                    }

                    if (c_files.Count == 0)
                        continue;

                    c_files.CheckFilesExist(args);
                    c_files.CheckFileAttributes(args);

                    ArrayList files = c_files.GetFilePairs();

                    // create a temporary directory for CABs
                    string cabtemp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(cabtemp);
                    args.WriteLine(string.Format("Writing CABs to \"{0}\"", cabtemp));

                    // compress new CABs
                    string cabname = string.IsNullOrEmpty(enumerator.Current.Key)
                        ? Path.Combine(cabtemp, "SETUP_%d.CAB")
                        : Path.Combine(cabtemp, string.Format("SETUP_{0}_%d.CAB", enumerator.Current.Key));

                    Compress cab = new Compress();
                    long currentSize = 0;
                    cab.evFilePlaced += delegate(string s_File, int s32_FileSize, bool bContinuation)
                    {
                        if (!bContinuation)
                        {
                            totalSize += s32_FileSize;
                            currentSize += s32_FileSize;
                            args.WriteLine(String.Format(" {0} - {1}", s_File, EmbedFileCollection.FormatBytes(s32_FileSize)));
                        }

                        return 0;
                    };
                    cab.CompressFileList(files, cabname, true, true, args.embedResourceSize);

                    // embed new CABs
                    args.WriteLine("Embedding CABs");
                    foreach (string cabfile in Directory.GetFiles(cabtemp))
                    {
                        args.WriteLine(string.Format(" {0} - {1}", Path.GetFileName(cabfile),
                            EmbedFileCollection.FormatBytes(new FileInfo(cabfile).Length)));
                        ResourceUpdate.WriteFile(args.output, new ResourceId("RES_CAB"),
                            new ResourceId(Path.GetFileNameWithoutExtension(cabfile)),
                            ResourceUtil.NEUTRALLANGID, cabfile);
                    }

                    StringBuilder fileslist = new StringBuilder();
                    fileslist.AppendLine(string.Format("{0} CAB size: {1}", 
                        string.IsNullOrEmpty(enumerator.Current.Key) ? "*" : enumerator.Current.Key,
                        EmbedFileCollection.FormatBytes(currentSize)));
                    fileslist.Append(" " + String.Join("\r\n ", c_files.GetFileValuesWithSize()));
                    allFilesList.Add(fileslist.ToString());

                    args.WriteLine(string.Format("Cleaning up \"{0}\"", cabtemp));
                    Directory.Delete(cabtemp, true);
                }

                args.WriteLine("Embedding CAB directory");
                StringBuilder filesDirectory = new StringBuilder();
                filesDirectory.AppendLine(string.Format("Total CAB size: {0}\r\n", EmbedFileCollection.FormatBytes(totalSize)));
                filesDirectory.AppendLine(string.Join("\r\n\r\n", allFilesList.ToArray()));
                byte[] filesDirectory_b = Encoding.Unicode.GetBytes(filesDirectory.ToString());
                ResourceUpdate.Write(args.output, new ResourceId("CUSTOM"), new ResourceId("RES_CAB_LIST"),
                    ResourceUtil.NEUTRALLANGID, filesDirectory_b);
            }

            if (! string.IsNullOrEmpty(args.manifest))
            {
                args.WriteLine(string.Format("Embedding manifest \"{0}\"", args.manifest));
                ManifestResource manifest = new ManifestResource();
                manifest.Manifest.Load(args.manifest);
                manifest.SaveTo(args.output);
            }

            args.WriteLine(string.Format("Successfully created \"{0}\"", args.output));
        }
    }
}
