using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReaderWriter.Test
{
  public class ExampleClass: ICanRead
  {

    //ICanRead.Version
    public int Version { get { return 3; } }

    public List<string> Values { get; private set; }
    public string StringValue { get; set; }

    public ExampleClass()
    {
      Values = new List<string>();
    }

    //ICanRead.ReadParts
    public IEnumerable<object> ReadParts(IReadFormatter formatter)
    {
      yield return formatter.Format("Values", Values);
      yield return formatter.Format("StringValue", StringValue);
    }

    //ICanRead.ReadFrom
    public void ReadFrom(IReader reader, int version)
    {
      Values = reader.List<string>().ToList();
      StringValue = reader.Property<string>();

      while (version < Version)
      {
        switch (version)
        {
          case 1:
            //convert version 1 data to 2
            break;

          case 2:
            //convert version 2 data to 3
            break;
        }
        version++;
      }
    }

  }
}
