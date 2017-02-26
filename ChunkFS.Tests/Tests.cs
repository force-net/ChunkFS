using System;
using System.IO;
using NUnit.Framework;

namespace ChunkFS.Tests
{
	[TestFixture]
	public class Tests
	{
		[Test]
		public void Test1()
		{
			File.WriteAllBytes(@"N:\t4\test1.bin", new byte[70000]);
			File.Delete(@"N:\t4\test2.bin");
			File.Move(@"N:\t4\test1.bin", @"N:\t4\test2.bin");
		}
	}
}