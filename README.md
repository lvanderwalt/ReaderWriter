# ReaderWriter

* Version-tolerant binary serialization: read old data with a new class, convert on-the-fly, and save the new version
* No need for [Serializable] attributes
* Create a snap-shot memento of an object tree, and restore it later
* Deep cloning for entire object trees
* Generate a text representation of an object tree (useful when unit-tests compare entire trees)

### Example

```cs
public class ExampleClass: ICanRead
{

  public List<ExampleClass> Values { get; private set; }
  public string StringValue { get; set; }

  public ExampleClass()
  {
    Values = new List<ExampleClass>();
  }

  #region ICanRead interface


  public int Version { get { return 3; } }

  public IEnumerable<object> ReadParts(IReadFormatter formatter)
  {
    yield return formatter.Format(nameof(Values), Values);
    yield return formatter.Format(nameof(StringValue), StringValue);
  }

  public void ReadFrom(IReader reader, int version)
  {
    Values = reader.List<ExampleClass>().ToList();
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

  #endregion

}

public void Test()
{
  var example = new ExampleClass { StringValue = "Value1" };
  example.Values.Add(new ExampleClass { StringValue = "Value2" });

  var stream = new MemoryStream();
  example.WriteTo(stream);

  var newInstance = new ExampleClass();
  stream.Position = 0;
  newInstance.ReadFrom(stream);
}
```

**NOTE:** See the _ReaderWriter.Test_ project for Nunit tests
