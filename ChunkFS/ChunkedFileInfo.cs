using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using DokanNet;
using FileAccess = System.IO.FileAccess;

namespace Force.ChunkFS
{
	public class ChunkedFileInfo : IDisposable
	{
		private readonly string _filePrefix;

		private readonly string _directory;

		private readonly string _fileFullPath;

		public FileAttributes? Attributes { get; set; }

		private Stream _currentReadStream;

		private long _currentReadIndex = -1;

		private Stream _currentWriteStream;

		private long _currentWriteIndex = -1;

		private string _currentWriteStreamFileName;

		public ChunkedFileInfo(string fileName)
			: this(fileName, FileMode.Open)
		{
		}

		public ChunkedFileInfo(string fileName, FileMode mode)
		{
			_filePrefix = Path.GetFileName(fileName);
			_directory = Path.GetDirectoryName(fileName);
			// meta file name with chunk suffix for better search and to ensure used won't try to open empty file by extension
			_fileFullPath = fileName + ChunkFSConstants.MetaFileExtension;
			if (mode == FileMode.Truncate || mode == FileMode.Create)
				DeleteAllChunks();
			// Console.WriteLine(fileName + " " + mode);
			if (mode == FileMode.Create || mode == FileMode.OpenOrCreate || mode == FileMode.Append || mode == FileMode.CreateNew)
				CreateRealFile();
		}

		private void CreateRealFile()
		{
			if (!File.Exists(_fileFullPath))
				File.Create(_fileFullPath).Close();
		}


		public DateTime? CreationTime { get; set; }
		public DateTime? LastAccessTime { get; set; }
		public DateTime? LastWriteTime { get; set; }

		public void DeleteAllChunks()
		{
			GetExistingFiles().ForEach(File.Delete);
		}

		private List<string> GetExistingFiles()
		{
			// does not return meta chunk
			return Directory.GetFiles(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + "*").Where(x => !x.EndsWith(ChunkFSConstants.MetaFileExtension))
				// .Concat(Directory.GetFiles(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + "*" + ChunkFSConstants.CompressedFileExtension))
				.ToList();
		}

		public void Write(byte[] buffer, long position)
		{
			// position from end
			if (position < 0)
			{
				if (!_length.HasValue) GetFileInfo();
				position = _length.GetValueOrDefault() + 1 + position;
			}

			var offset = 0;
			var count = buffer.Length;
			while (count > 0)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenWriteChunk(requiredChunk);
				var chunkOffset = (int) (position % ChunkFSConstants.ChunkSize);
				_currentWriteStream.Position = chunkOffset;

				var toWrite = Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				_currentWriteStream.Write(buffer, offset, toWrite);
				offset += toWrite;
				count -= toWrite;
				position += toWrite;
			}

			if (_length.HasValue && position > _length.Value)
				_length = position;
		}

		public int Read(byte[] buffer, long position)
		{
			// position from end
			if (position < 0)
			{
				if (!_length.HasValue) GetFileInfo();
				position = _length.GetValueOrDefault() + 1 - position;
			}

			var offset = 0;
			var count = buffer.Length;
			while (offset < buffer.Length)
			{
				var requiredChunk = position / ChunkFSConstants.ChunkSize;
				OpenReadChunk(requiredChunk);
				var chunkOffset = position % ChunkFSConstants.ChunkSize;
				_currentReadStream.Position = chunkOffset;

				var toRead = (int)Math.Min(Math.Min(ChunkFSConstants.ChunkSize, count), ChunkFSConstants.ChunkSize - chunkOffset);
				var readed = _currentReadStream.Read(buffer, offset, toRead);
				if (readed == 0)
					break;
				offset += readed;
				count -= readed;
				position += readed;
			}

			return offset;
		}

		private void OpenReadChunk(long requiredChunk)
		{
			if (_currentReadIndex != requiredChunk || _currentReadStream == null)
			{
				_currentReadStream?.Dispose();
				var path = Path.Combine(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + requiredChunk.ToString("0000", CultureInfo.InvariantCulture));
				if (File.Exists(path))
					_currentReadStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
				else
				{
					// compressed data
					_currentReadStream = CompressorQueue.Instance.DecompressFile(path);
				}
				_currentReadIndex = requiredChunk;
			}
		}

		private void OpenWriteChunk(long requiredChunk)
		{
			if (_currentWriteIndex != requiredChunk || _currentWriteStream == null)
			{
				// will set data after our real close
				_currentWriteStream?.Flush();
				_currentWriteStream?.Dispose();
				if (_currentWriteStreamFileName != null)
					CompressorQueue.Instance.AddFileToCompressQueue(_currentWriteStreamFileName);

				_currentWriteStreamFileName = Path.Combine(_directory, _filePrefix + ChunkFSConstants.ChunkExtension + requiredChunk.ToString("0000", CultureInfo.InvariantCulture));

				if (File.Exists(_currentWriteStreamFileName + ChunkFSConstants.CompressedFileExtension) &&
				    !File.Exists(_currentWriteStreamFileName))
				{
					// we already read this chunk, will reopen it
					if (_currentReadStream != null && _currentReadIndex == _currentWriteIndex)
					{
						// flushing all memory data to uncompressed file
						if (_currentReadStream is MemoryStream)
						{
							_currentReadStream.Position = 0;
							using (var f = File.OpenWrite(_currentWriteStreamFileName))
								_currentReadStream.CopyTo(f);
							CompressorQueue.Instance.DeleteCompressedFile(_currentWriteStreamFileName);
							// reopening read stream
							_currentReadStream = null;
							OpenReadChunk(_currentReadIndex);
						}
					}
					else
					{
						// just uncompress it
						var df = CompressorQueue.Instance.DecompressFile(_currentWriteStreamFileName);
						using (var f = File.OpenWrite(_currentWriteStreamFileName))
							df.CopyTo(f);
						CompressorQueue.Instance.DeleteCompressedFile(_currentWriteStreamFileName);
					}
				}

				_currentWriteStream = new FileStream(_currentWriteStreamFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
				_currentWriteIndex = requiredChunk;
			}
		}

		public void Close()
		{
			_currentReadStream?.Dispose();
			_currentReadStream = null;

			_currentWriteStream?.Dispose();
			_currentWriteStream = null;
			if (_currentWriteStreamFileName != null)
				CompressorQueue.Instance.AddFileToCompressQueue(_currentWriteStreamFileName);

			if (!File.Exists(_fileFullPath))
				return;

			if (Attributes != null)
				File.SetAttributes(_fileFullPath, Attributes.Value);
			if (CreationTime != null)
				File.SetCreationTime(_fileFullPath, CreationTime.Value);
			if (LastAccessTime != null)
				File.SetLastAccessTime(_fileFullPath, LastAccessTime.Value);
			if (LastWriteTime != null)
				File.SetLastWriteTime(_fileFullPath, LastWriteTime.Value);
		}

		public void Dispose()
		{
			Close();
		}

		public void Flush()
		{
			_currentWriteStream?.Flush();
		}

		public void MoveFile(string newpath)
		{
			File.Move(_fileFullPath, newpath + ChunkFSConstants.MetaFileExtension);
			GetExistingFiles().ForEach(x =>
			{
				var idx = x.IndexOf(ChunkFSConstants.ChunkExtension, StringComparison.Ordinal);
				// .chunk001 or .chunk002.blz
				var chunkName = x.Remove(0, idx);

				File.Move(x, newpath + chunkName);
				// trying to re-add compressed data to queue (copy and move situation)
				if (!x.EndsWith(ChunkFSConstants.CompressedFileExtension))
					CompressorQueue.Instance.AddFileToCompressQueueIfNotExists(newpath + chunkName, x);
			});
		}

		public void DeleteFile()
		{
			DeleteAllChunks();
			File.Delete(_fileFullPath);
		}

		public void SetLength(long length)
		{
			if (_length.HasValue && _length == length)
				return;
			var maxChunkNumber = Math.Max((length - 1) / ChunkFSConstants.ChunkSize, 0);
			var lastChunkSize = length - maxChunkNumber * ChunkFSConstants.ChunkSize;
			GetExistingFiles()
				.Select(x => x.EndsWith(ChunkFSConstants.CompressedFileExtension) ? x.Substring(0, x.Length - ChunkFSConstants.CompressedFileExtension.Length) : x)
				.Where(x => Convert.ToInt32(Path.GetExtension(x).Remove(0, ChunkFSConstants.ChunkExtension.Length)) > maxChunkNumber)
				.ToList()
				.ForEach(x =>
				{
					File.Delete(x);
					CompressorQueue.Instance.DeleteCompressedFile(x);
				});

			for (var i = 0; i < maxChunkNumber; i++)
			{
				OpenWriteChunk(i);
				_currentWriteStream.SetLength(ChunkFSConstants.ChunkSize);
			}

			OpenWriteChunk(maxChunkNumber);
			_currentWriteStream.SetLength(lastChunkSize);
			_length = length;
		}

		public FileSecurity GetAccessControl()
		{
			return File.GetAccessControl(_fileFullPath);
		}

		public void SetAccessControl(FileSecurity security)
		{
			// think about setting access rights to all files
			File.SetAccessControl(_fileFullPath, security);
			// GetExistingFiles().ForEach(x => File.SetAccessControl(x, security));
		}

		public bool Exists()
		{
			return File.Exists(_fileFullPath);
		}

		public FileAttributes GetAttributes()
		{
			return Attributes ?? File.GetAttributes(_fileFullPath);
		}

		private long? _length;

		public FileInformation GetFileInfo()
		{
			var finfo = new FileInfo(_fileFullPath);
			var l = new FileInformation
			{
				Attributes = Attributes ?? finfo.Attributes,
				CreationTime = CreationTime ?? finfo.CreationTime,
				LastAccessTime = LastAccessTime ?? finfo.LastAccessTime,
				FileName = _filePrefix,
				LastWriteTime = LastWriteTime ?? finfo.LastWriteTime,
				Length = _length ?? (_length = GetExistingFiles().Sum(x => x.EndsWith(ChunkFSConstants.CompressedFileExtension) ? ChunkFSConstants.ChunkSize : new FileInfo(x).Length)).Value
			};
			// Console.WriteLine("GetFileInfo: " + _fileFullPath + " " + GetExistingFiles().Count);

			return l;
		}

	}
}