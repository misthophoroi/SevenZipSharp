﻿namespace SevenZip.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using SevenZip;

    using NUnit.Framework;
    using System.Reflection;

    [TestFixture]
    public class SevenZipExtractorTests : TestBase
    {
        public static List<TestFile> TestFiles
        {
            get
            {
                var result = new List<TestFile>();
                //with netstandard2.0 TestContext.CurrentContext.TestDirectory pointed to ~/.nuget/packages/nunit/3.10.1/lib/netstandard2.0
                foreach (var file in Directory.GetFiles(Path.Combine(Assembly.GetExecutingAssembly().Location, "..", "TestData")))
                {
                    if (file.Contains("multi") || file.Contains("long_path"))
                    {
                        continue;
                    }

                    result.Add(new TestFile(file));
                }

                return result;
            }
        }

        [Test]
        public void ExtractFilesTest()
        {
            using (var extractor = new SevenZipExtractor(@"TestData\multiple_files.7z"))
            {
                for (var i = 0; i < extractor.ArchiveFileData.Count; i++)
                {
                    extractor.ExtractFiles(OutputDirectory, extractor.ArchiveFileData[i].Index);
                }

                Assert.AreEqual(3, Directory.GetFiles(OutputDirectory).Length);
            }
        }

        [Test]
        public void ExtractSpecificFilesTest()
        {
            using (var extractor = new SevenZipExtractor(@"TestData\multiple_files.7z"))
            {
                extractor.ExtractFiles(OutputDirectory, 0, 2);
                Assert.AreEqual(2, Directory.GetFiles(OutputDirectory).Length);
            }

            Assert.AreEqual(2, Directory.GetFiles(OutputDirectory).Length);
            Assert.Contains(Path.Combine(OutputDirectory, "file1.txt"), Directory.GetFiles(OutputDirectory));
            Assert.Contains(Path.Combine(OutputDirectory, "file3.txt"), Directory.GetFiles(OutputDirectory));
        }

        [Test]
        public void ExtractArchiveMultiVolumesTest()
        {
            using (var extractor = new SevenZipExtractor(@"TestData\multivolume.part0001.rar"))
            {
                extractor.ExtractArchive(OutputDirectory);
            }

            Assert.AreEqual(1, Directory.GetFiles(OutputDirectory).Length);
            Assert.IsTrue(File.ReadAllText(Directory.GetFiles(OutputDirectory)[0]).StartsWith("Lorem ipsum dolor sit amet"));
        }

        [Test]
        public void ExtractionWithCancellationTest()
        {
            using (var tmp = new SevenZipExtractor(@"TestData\multiple_files.7z"))
            {
                tmp.FileExtractionStarted += (s, e) =>
                {
                    if (e.FileInfo.Index == 2)
                    {
                        e.Cancel = true;
                    }
                };
               
                tmp.ExtractArchive(OutputDirectory);

                Assert.AreEqual(2, Directory.GetFiles(OutputDirectory).Length);
            }
        }

        [Test]
        public void ExtractionFromStreamTest()
        {
            // TODO: Rewrite this to test against more/all TestData archives.
            using (var fstream = File.OpenRead(@"TestData\multiple_files.7z"))
            {
                using (var tmp = new SevenZipExtractor(fstream, leaveOpen: false))
                {
                    tmp.ExtractArchive(OutputDirectory);
                }
                Assert.AreEqual(3, Directory.GetFiles(OutputDirectory).Length);
                Assert.Throws<ObjectDisposedException>( () => fstream.Seek(0, SeekOrigin.Begin) );
            }
        }

        [Test]
        public void ExtractionFromStreamLeaveOpenTest()
        {
            using (var fstream = File.OpenRead(@"TestData\multiple_files.7z"))
            {
                using (var tmp = new SevenZipExtractor(fstream, leaveOpen: true))
                {
                    tmp.ExtractArchive(OutputDirectory);
                }
                Assert.AreEqual(3, Directory.GetFiles(OutputDirectory).Length);
                Assert.DoesNotThrow(() => fstream.Seek(0, SeekOrigin.Begin));
            }
        }

        [Test]
        public void ExtractionToStreamTest()
        {
            using (var tmp = new SevenZipExtractor(@"TestData\multiple_files.7z"))
            {
                using (var fileStream = new FileStream(Path.Combine(OutputDirectory, "streamed_file.txt"), FileMode.Create))
                {
                    tmp.ExtractFile(1, fileStream);
                }
            }

            Assert.AreEqual(1, Directory.GetFiles(OutputDirectory).Length);

            var extractedFile = Directory.GetFiles(OutputDirectory)[0];

            Assert.AreEqual("file2", File.ReadAllText(extractedFile));
        }

        [Test]
        public void DetectMultiVolumeIndexTest()
        {
            using (var tmp = new SevenZipExtractor(@"TestData\multivolume.part0001.rar"))
            {
                Assert.IsTrue(tmp.ArchiveProperties.Any(x => x.Name.Equals("IsVolume") && x.Value != null && x.Value.Equals(true)));
                Assert.IsTrue(tmp.ArchiveProperties.Any(x => x.Name.Equals("VolumeIndex") && x.Value != null && Convert.ToInt32(x.Value) == 0));
            }

            using (var tmp = new SevenZipExtractor(@"TestData\multivolume.part0002.rar"))
            {
                Assert.IsTrue(tmp.ArchiveProperties.Any(x => x.Name.Equals("IsVolume") && x.Value != null && x.Value.Equals(true)));
                Assert.IsFalse(tmp.ArchiveProperties.Any(x => x.Name.Equals("VolumeIndex") && x.Value != null && Convert.ToInt32(x.Value) == 0));
            }
        }

        [Test]
        public void ThreadedExtractionTest()
        {
	        var destination1 = Path.Combine(OutputDirectory, "t1");
	        var destination2 = Path.Combine(OutputDirectory, "t2");

			var t1 = new Thread(() =>
            {
                using (var tmp = new SevenZipExtractor(@"TestData\multiple_files.7z"))
                {
                    tmp.ExtractArchive(destination1);
                }
            });
            var t2 = new Thread(() =>
            {
				using (var tmp = new SevenZipExtractor(@"TestData\multiple_files.7z"))
				{
                    tmp.ExtractArchive(destination2);
                }
            });

            t1.Start();
            t2.Start();
            t1.Join();
            t2.Join();

			Assert.IsTrue(Directory.Exists(destination1));
	        Assert.IsTrue(Directory.Exists(destination2));
			Assert.AreEqual(3, Directory.GetFiles(destination1).Length);
	        Assert.AreEqual(3, Directory.GetFiles(destination2).Length);
		}

        [Test]
        public void ExtractArchiveWithLongPath()
        {
            using (var extractor = new SevenZipExtractor(@"TestData\long_path.7z"))
            {
#if NET462
                //dotNet462 can access very long path by UNC - like \\?\disk:\folder\
                var uncOutputDirectory = @"\\?\"+ Path.GetFullPath(OutputDirectory);
                Assert.DoesNotThrow(() => extractor.ExtractArchive(uncOutputDirectory));
#elif NETCOREAPP2_1
                //netstandard2.0 does not has any problems with long path from sample
                Assert.DoesNotThrow(() => extractor.ExtractArchive(OutputDirectory));
#else
                Assert.Throws<PathTooLongException>(() => extractor.ExtractArchive(OutputDirectory));
#endif
            }
        }

        [Test, TestCaseSource(nameof(TestFiles))]
        public void ExtractDifferentFormatsTest(TestFile file)
        {
            using (var extractor = new SevenZipExtractor(file.FilePath))
            {
                extractor.ExtractArchive(OutputDirectory);
                Assert.AreEqual(1, Directory.GetFiles(OutputDirectory).Length);
            }
        }
    }

    /// <summary>
    /// Simple wrapper to get better names for ExtractDifferentFormatsTest results.
    /// </summary>
    public class TestFile
    {
        public string FilePath { get; }

        public TestFile(string filePath)
        {
            FilePath = filePath;
        }

        public override string ToString()
        {
            return Path.GetFileName(FilePath);
        }
    }
}
