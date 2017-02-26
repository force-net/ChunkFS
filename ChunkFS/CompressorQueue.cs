using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Force.Blazer;
using Force.Blazer.Algorithms;

namespace Force.ChunkFS
{
	public class CompressorQueue
	{
		private readonly ConcurrentQueue<string> _fileQueue = new ConcurrentQueue<string>();

		public void AddFileToCompressQueue(string fileName)
		{
			_fileQueue.Enqueue(fileName);
		}

		public void AddFileToCompressQueueIfNotExists(string fileName, string checkFileName)
		{
			if (!_fileQueue.Contains(checkFileName))
				_fileQueue.Enqueue(fileName);
		}

		public CompressorQueue()
		{
			Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);
		}

		private void ProcessQueue()
		{
			while (true)
			{
				try
				{
					string result;
					if (_fileQueue.TryPeek(out result))
					{
						if (DateTime.Now.Subtract(File.GetLastWriteTime(result)) > TimeSpan.FromSeconds(10))
						{
							_fileQueue.TryDequeue(out result);
							try
							{
								CompressFile(result);
							}
							catch (IOException e)
							{
								// it is normal here to skip action on IO problem
								// Console.WriteLine(e);
								// throw;
							}

							continue;
						}
					}
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
					// throw;
				}
				Thread.Sleep(1000);
			}
		}

		private void CompressFile(string fileName)
		{
			// nothing to do
			if (!File.Exists(fileName))
				return;
			var finfo = new FileInfo(fileName);
			var lwt = finfo.LastWriteTimeUtc;
			// not full chunk. ok
			if (finfo.Length != ChunkFSConstants.ChunkSize)
				return;

			var bytes = File.ReadAllBytes(fileName);
			// invalid data
			if (bytes.Length != ChunkFSConstants.ChunkSize)
				return;
			var encoder = BlazerCompressionOptions.CreateStream().Encoder;
			encoder.Init(bytes.Length);
			var binfo = encoder.Encode(bytes, 0, bytes.Length);
			var compressedFileName = fileName + ChunkFSConstants.CompressedFileExtension;

			// we're really compress data
			if (binfo.Count < bytes.Length)
			{
				using (var f = File.OpenWrite(compressedFileName))
				{
					f.Write(binfo.Buffer, binfo.Offset, binfo.Count);
				}

				// TODO: more correct synchronizing
				if (lwt == File.GetLastWriteTimeUtc(fileName))
					File.Delete(fileName);

				Console.WriteLine("Compressed " + fileName + " " + (100 * binfo.Count / bytes.Length) + "%");
			}
			else
			{
				Console.WriteLine("Not compressed " + fileName);
				// cannot compress, no sense to store compressed files
				DeleteCompressedFile(fileName);
			}
		}

		public static CompressorQueue Instance { get; } = new CompressorQueue();

		public MemoryStream DecompressFile(string path)
		{
			var opts = BlazerDecompressionOptions.CreateDefault();
			opts.SetDecoderByAlgorithm(BlazerAlgorithm.Stream);

			var compressedFileName = path + ChunkFSConstants.CompressedFileExtension;
			// zero file. it is ok
			if (!File.Exists(compressedFileName))
				return new MemoryStream();
			var bytes = File.ReadAllBytes(compressedFileName);

			opts.Decoder.Init(ChunkFSConstants.ChunkSize);
			var binfo = opts.Decoder.Decode(bytes, 0, bytes.Length, true);
			return new MemoryStream(binfo.Buffer, binfo.Offset, binfo.Count);
		}

		public void DeleteCompressedFile(string fileName)
		{
			var compressedFileName = fileName + ChunkFSConstants.CompressedFileExtension;
			if (File.Exists(compressedFileName))
				File.Delete(compressedFileName);
		}
	}
}