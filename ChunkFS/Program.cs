using System;
using DokanNet;

namespace Force.ChunkFS
{
	internal class Program
	{
		public static void Main(string[] args)
		{
			try
			{
				if (args.Length < 2)
				{
					Console.WriteLine("Usage ChunkFS.exe storagepath virtualpath");
					return;
				}

				var mirror = new DokanOperations(new WrappedPathFileStorage(new BackDirFileStorage(), args[0]));
				mirror.Mount(args[1], DokanOptions.RemovableDrive, 4);

				// Console.WriteLine(@"Mounted");
			}
			catch (DokanException ex)
			{
				Console.WriteLine(@"Error: " + ex.Message);
			}
		}
	}
}