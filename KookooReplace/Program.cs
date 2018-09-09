using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Core;
using CommandLine;
using ICSharpCode.SharpZipLib.Zip;
using ZetaLongPaths;

namespace KookooReplace
{
    class Program
    {

        class Options
        {
            [Option('r', "read", Required = true, HelpText = "Input files to be processed.")]
            public IEnumerable<string> InputFiles { get; set; }

            // Omitting long name, defaults to name of property, ie "--verbose"
            [Option(
              Default = false,
              HelpText = "Prints all messages to standard output.")]
            public bool Verbose { get; set; }

            [Option("stdin",
              Default = false,
              HelpText = "Read from stdin")]
            public bool stdin { get; set; }

            [Value(0, MetaName = "offset", HelpText = "File offset.")]
            public long? Offset { get; set; }
        }

        static void Main(string[] args)
        {
            var fileNameOfPayload = String.Empty;

            if (args.Length < 1 || !File.Exists(args[0]))
            {
                Console.WriteLine(
                    "I need a file name of the file to replace found files with...Please restart with the file name provided.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            fileNameOfPayload = args[0];

            var archiveExtension = String.Empty;

            if (args.Length > 1)
            {
                archiveExtension = args[1];
            }

            var payloadFileExistis = File.Exists(fileNameOfPayload);
            var currentDirectory = Directory.GetCurrentDirectory();


            if (!payloadFileExistis)
            {
                Console.WriteLine(
                    $"Can't find file to replace occurences with. {fileNameOfPayload} is not in the current folder {currentDirectory}.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }

            var filePathToPayload =
                Directory.GetFiles(currentDirectory, fileNameOfPayload, SearchOption.TopDirectoryOnly).FirstOrDefault();

            var allDirectoriesToSearchForFilesToReplace = Directory.GetDirectories(currentDirectory, "*",
                SearchOption.TopDirectoryOnly);

            var archivePattern = String.IsNullOrEmpty(archiveExtension) ? "*.zip" : "*." + archiveExtension;

            foreach (var directory in allDirectoriesToSearchForFilesToReplace)
            {
                Console.WriteLine($"---Processing directory {directory}");

                var dirObject = new ZlpDirectoryInfo(directory);

                var filesToReplaceInCurrentDirAndDown = dirObject.GetFiles(fileNameOfPayload, SearchOption.AllDirectories);

                foreach (var fileToReplace in filesToReplaceInCurrentDirAndDown)
                {
                    bool alreadyReplaced = fileToReplace.Length == new FileInfo(filePathToPayload).Length &&
                                           fileToReplace.ReadAllBytes()
                                               .SequenceEqual(File.ReadAllBytes(filePathToPayload));

                    if (!alreadyReplaced)
                    {
                        Console.WriteLine($"---------Found file {fileToReplace}. Replacing...");
                        File.Copy(filePathToPayload, fileToReplace.OriginalPath, true);
                    }
                }

                var archivesToSearchInCurrentDirAndDown = dirObject.GetFiles(archivePattern, SearchOption.AllDirectories);

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

                            var testValue = entry.Name.LastIndexOf("/") >= 0 ? entry.Name.Substring(entry.Name.LastIndexOf("/") + 1) : entry.Name;

                            if (testValue == fileNameOfPayload)
                            {
                                filesToUpdate.Add(entry.Name);
                            }
                        }
                    }

                    if (filesToUpdate.Count > 0)
                    {
                        streamToFile = archivePath.OpenWrite();
                        var idealFileStream = File.OpenRead(filePathToPayload);

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
                                Console.WriteLine($"------filePathToPayload was {filePathToPayload}");
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
