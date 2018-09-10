using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using CommandLine;
using CommandLine.Text;
using ICSharpCode.SharpZipLib.Zip;
using ZetaLongPaths;

namespace KookooReplace
{
    class Program
    {
        public enum Mode
        {
            Folder = 1,
            SQLImage = 2
        }

        public class Options
        {
            [Option('r',
                "rootDirectory",
                Default = ".",
                Required = false,
                HelpText = "Defines a directory which serves as a root for recursive \"deep dive\" to find files to replace with etalon file.")]
            public string RootDirectory { get; set; }

            [Option('f',
                "fileToReplaceWith",
                Required = true,
                HelpText = "A file path or a name in case located in the current folder of the etalon file that the other files with the same name are recursivelly replaced.")]
            public string File { get; set; }

            [Option('m', 
                "mode", 
                Required = false, 
                HelpText = "Kookoo Replace can operate either in folder or sql image mode to replace file in folder or archive. Folder mode is assumed default.", 
                Default = Mode.Folder)]
            public Mode Mode { get; set; }

            // Omitting long name, defaults to name of property, ie "--verbose"
            [Option('a', "archive",
                Required = false,
                HelpText = "Defines which type of archives will Kookoo Replace look into to find files to replace. Can be a | separated array of extensions: jar|orb|zip.",
                Separator = '|')]
            public IEnumerable<string> ArchiveExtensions { get; set; }

            [Option('t', 
                "Table", 
                Required = false, 
                Default = " ",
                HelpText = "Defines a table name from which the images will be extracted for procession. If this parameter is specified, 'f' and 'i' params must also be specified.")]
            public string Table { get; set; }

            [Option('F',
                "fileNameColumn",
                Required = false,
                HelpText = "Defines a column name where a script can find a file name to materialize the image to for futher procession.",
                Default = " "
                )]
            public string FileNameColumn { get; set; }


            [Option('i',
                "imageColumn",
                Required = false,
                HelpText = "Defines a column name where a script can find an image to materialize the image to for futher procession.",
                Default = " "
            )]
            public string ImageColumn { get; set; }

            [Option('c',
                "connectionString",
                Required = false,
                HelpText = "In case SQL image mode is used, a connection string to a database where the target images are located.")]
            public string ConnectionString { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed<Options>(o =>
                {
                    if (o.Mode == Mode.Folder) RunFolderMode(o);
                    //else RunSQLMode(o);
                }).WithNotParsed(OutputErrorsAndExit);

            
        }

        private static void OutputErrorsAndExit(IEnumerable<Error> errors)
        {
            Console.WriteLine("Error parsing arguments. Details below:");
            foreach (var error in errors)
            {
                Console.WriteLine(error);
            }
            Environment.Exit(1);
        }

        public static void RunFolderMode(Options o)
        {
            var payloadFilePath = Path.GetFullPath(o.File);

            var payloadFileExistis = File.Exists(payloadFilePath);
            var rootDirectory = o.RootDirectory;

            if (!payloadFileExistis)
            {
                Console.WriteLine(
                    $"Can't find file to replace occurences with. {payloadFilePath} does not point to an existing file.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            var fileNameOfPayload = Path.GetFileName(payloadFilePath);

            var allDirectoriesToSearchForFilesToReplace = Directory.GetDirectories(rootDirectory, "*",
                SearchOption.TopDirectoryOnly);

            if (File.Exists(rootDirectory + "\\" + fileNameOfPayload))
            {
                allDirectoriesToSearchForFilesToReplace = allDirectoriesToSearchForFilesToReplace
                    .Concat(new string[] {rootDirectory}).ToArray();
            }

            foreach (var directory in allDirectoriesToSearchForFilesToReplace)
            {
                Console.WriteLine($"---Processing directory {directory}");

                var dirObject = new ZlpDirectoryInfo(directory);

                var filesToReplaceInCurrentDirAndDown = dirObject.GetFiles(fileNameOfPayload, SearchOption.AllDirectories);

                foreach (var fileToReplace in filesToReplaceInCurrentDirAndDown)
                {
                    bool alreadyReplaced = fileToReplace.Length == new FileInfo(payloadFilePath).Length &&
                                           fileToReplace.ReadAllBytes()
                                               .SequenceEqual(File.ReadAllBytes(payloadFilePath));

                    if (!alreadyReplaced)
                    {
                        Console.WriteLine($"---------Found file {fileToReplace}. Replacing...");
                        File.Copy(payloadFilePath, fileToReplace.OriginalPath, true);
                    }
                }

                foreach (var archiveExtension in o.ArchiveExtensions)
                {
                    var archivesToSearchInCurrentDirAndDown =
                        dirObject.GetFiles("*." + archiveExtension, SearchOption.AllDirectories);

                    foreach (var archiveToSearch in archivesToSearchInCurrentDirAndDown)
                    {
                        Console.WriteLine($"------Processing archive {archiveToSearch}");
                        List<string> filesToUpdate = new List<string>();

                        Console.WriteLine($"------Openning file to search for target entries...");

                        var archivePath = new ZlpFileInfo(archiveToSearch.OriginalPath);
                        var streamToFile = archivePath.OpenRead();
                        using (ZipFile zip = new ZipFile(streamToFile))
                        {
                            for (int i = 0; i < zip.Count; i++)
                            {
                                var entry = zip[i];

                                var testValue = entry.Name.LastIndexOf("/") >= 0
                                    ? entry.Name.Substring(entry.Name.LastIndexOf("/") + 1)
                                    : entry.Name;

                                if (testValue == fileNameOfPayload)
                                {
                                    filesToUpdate.Add(entry.Name);
                                }
                            }
                        }

                        if (filesToUpdate.Count > 0)
                        {
                            streamToFile = archivePath.OpenWrite();
                            var idealFileStream = File.OpenRead(payloadFilePath);

                            Console.BackgroundColor = ConsoleColor.DarkGreen;
                            Console.WriteLine($"------Openning file to update found entries...");
                            Console.ResetColor();

                            try
                            {
                                foreach (var fileToUpdate in filesToUpdate)
                                {
                                    UpdateZipInMemory(streamToFile, idealFileStream, fileToUpdate);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.BackgroundColor = ConsoleColor.Red;
                                Console.WriteLine($"------Can't update archive {archiveToSearch}. File is locked.");
                                Console.WriteLine($"------payloadFilePath was {payloadFilePath}");
                                Console.WriteLine(ex.Message);
                                Console.WriteLine(ex.StackTrace);
                                Console.WriteLine(ex.InnerException?.Message);
                                Console.ResetColor();
                            }

                        }

                        Console.BackgroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine("------Finished processing archive.");
                        Console.ResetColor();
                    }
                }


                Console.WriteLine("---Finished processing directory.");
            }

            Console.WriteLine("Done. Exiting...");
            Environment.Exit(0);
        }

        /// <summary>
        /// Updates a zip file (in a disk or memorystream) adding the entry contained in the second stream.
        /// </summary>
        /// <param name="zipStream">Zip file, could be a disk or memory stream. Must be seekable. </param>
        /// <param name="entryStream">Stream containing a file to be added. </param>
        /// <param name="entryName">Name to appear in the zip entry. </param>
        /// 
        private static void UpdateZipInMemory(Stream zipStream, Stream entryStream, String entryName)
        {

            // The zipStream is expected to contain the complete zipfile to be updated
            ZipFile zipFile = new ZipFile(zipStream);

            zipFile.BeginUpdate();

            // To use the entryStream as a file to be added to the zip,
            // we need to put it into an implementation of IStaticDataSource.
            CustomStaticDataSource sds = new CustomStaticDataSource();
            sds.SetStream(entryStream);

            // If an entry of the same name already exists, it will be overwritten; otherwise added.
            zipFile.Add(sds, entryName);

            // Both CommitUpdate and Close must be called.
            zipFile.CommitUpdate();
            // Set this so that Close does not close the memorystream
            zipFile.IsStreamOwner = false;
            zipFile.Close();

            // Reposition to the start for the convenience of the caller.
            zipStream.Position = 0;
        }

        public class CustomStaticDataSource : IStaticDataSource
        {
            private Stream _stream;
            // Implement method from IStaticDataSource
            public Stream GetSource()
            {
                return _stream;
            }

            // Call this to provide the memorystream
            public void SetStream(Stream inputStream)
            {
                _stream = inputStream;
                _stream.Position = 0;
            }
        }

    }

}
