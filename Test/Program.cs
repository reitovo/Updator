// See https://aka.ms/new-console-template for more information

using Updator.Common.ChecksumProvider;

Console.WriteLine("Hello, World!");

var a = new ChecksumCosCrc64();
var b = new ChecksumLocalCrc64();

var ms = new byte[100000];
Random.Shared.NextBytes(ms); 

Console.WriteLine(await a.CalculateChecksum(new MemoryStream(ms)));
Console.WriteLine(await b.CalculateChecksum(new MemoryStream(ms)));