using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections;
using System.Runtime.Serialization;

//defined in the System namespace, so extensions can work from anywhere, without a direct dependency to ReaderWriterExtensions
namespace System
{
  /// <summary>
  /// Exposes extensions via ICanRead interface to enable Binary Serialization on complex structures, without the Serializable attribute. 
  /// 
  /// * Version-tolerant persistance: read old data with a new class, convert on-the-fly, and save the new version
  /// * Create a snap-shot memento of an object tree, and restore it later
  /// * Deep cloning for entire object trees
  /// * Generate a text representation of an object tree (useful when unit-tests compare entire trees)
  /// </summary>
  public static class ReaderWriterExtensions
  {

    /// <summary>
    /// Returns a snapshot memento of an object
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static IMemento GetMemento(this ICanRead self)
    {
      return self.GetMemento(new MemoryStream());
    }

    /// <summary>
    /// Initialize an object from a stream, then returns a snapshot memento of it
    /// </summary>
    /// <param name="self"></param>
    /// <param name="stream"></param>
    /// <returns></returns>
    public static IMemento GetMemento(this ICanRead self, Stream stream)
    {
      IMemento memento = new StreamMemento(stream, new FormatterStrategy());
      memento.ReadFrom(self);
      return memento;
    }

    /// <summary>
    /// Initialize an object from a stream
    /// </summary>
    /// <param name="self"></param>
    /// <param name="stream"></param>
    public static void ReadFrom(this ICanRead self, Stream stream)
    {
      var memento = new StreamMemento(stream, new FormatterStrategy());
      self.ReadFrom(memento);
    }

    /// <summary>
    /// Save an object to a stream
    /// </summary>
    /// <param name="self"></param>
    /// <param name="stream"></param>
    public static void WriteTo(this ICanRead self, Stream stream)
    {
      IMemento memento = new StreamMemento(stream, new FormatterStrategy());
      memento.ReadFrom(self);
    }

    /// <summary>
    /// Returns a snapshot memento of an object, into a byte array
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static byte[] AsByteArray(this IAmReadable self)
    {
      var stream = new MemoryStream();
      IMemento memento = new StreamMemento(stream, new FormatterStrategy());
      memento.ReadFrom(self);
      stream.Position = 0;
      return stream.ToArray();
    }    

    /// <summary>
    /// Restores an object from a byte array memento
    /// </summary>
    /// <param name="self"></param>
    /// <param name="byteArray"></param>
    public static void ReadFrom(this ICanRead self, byte[] byteArray)
    {
      var stream = new MemoryStream();
      var memento = new StreamMemento(stream, new FormatterStrategy());
      stream.Write(byteArray, 0, byteArray.Length);
      stream.Position = 0;
      self.ReadFrom(memento);
    }

    /// <summary>
    /// Deep-clone one object into another
    /// </summary>
    /// <param name="self"></param>
    /// <param name="other"></param>
    public static void CloneTo(this ICanRead self, ICanRead other)
    {
      other.ReadFrom(new ContiniousReader(self, new FormatterStrategy()));
    }

    /// <summary>
    /// Returns a deep-clone of an object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="self"></param>
    /// <returns></returns>
    public static T Clone<T>(this T self)
        where T : ICanRead, new()
    {
      var result = new T();
      self.CloneTo(result);
      return result;
    }

    /// <summary>
    /// Writes a textual representation of an object to a TextWriter
    /// </summary>
    /// <param name="self"></param>
    /// <param name="writer"></param>
    public static void WriteTo(this IAmReadable self, TextWriter writer)
    {
      var formatter = new ObjectTextFormatter(writer, 0);
      formatter.Write(self);
    }

    /// <summary>
    /// Returns a textual representation of an object
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static string AsString(this ICanRead self)
    {
      StringBuilder builder = new StringBuilder();
      StringWriter writer = new StringWriter(builder);
      self.WriteTo(writer);
      return builder.ToString();
    }

    private static void ReadFrom(this ICanRead self, IUnformattedReader reader)
    {
      if (reader != null)
      {
        reader.Read((version) =>
        {
          self.ReadFrom(reader, version);
        });
      }
    }

    private class FormatterStrategy : IFormatterStrategy
    {
      const int Version = 1;

      public object Format<T>(string name, T value)
      {
        var readable = value as ICanRead;
        if (readable != null)
        {
          return PropertyParts(name, readable);
        }

        var list = value as ICollection;
        if (list != null)
        {
          return ListParts(name, list);
        }

        return new PropertyHolder
        {
          Name = name,
          Value = value
        };
      }


      IEnumerable<object> PropertyParts(string name, ICanRead value)
      {
        yield return new PropertyHolder
        {
          Name = name,
          CanRead = true
        };
        foreach (var item in ReadFormatted(value))
        {
          yield return item;
        }
      }

      private IEnumerable<object> ListParts(string name, ICollection value)
      {
        yield return new ListHolder
        {
          Length = value.Count,
          Name = name
        };
        foreach (var item in value)
        {
          var readable = item as ICanRead;
          var itemType = item.GetType();
          var itemHolder = new ListItemHolder();

          if (readable == null)
          {
            itemHolder.Value = item;
          }
          else
          {
            itemHolder.CanRead = true;
          }
          yield return itemHolder;

          if (readable != null)
          {
            foreach (var part in ReadFormatted(readable))
            {
              yield return part;
            };
          }
        }
      }

      public IEnumerable<object> ReadFormatted(IAmReadable instance)
      {
        yield return Version;
        yield return new ObjectHeader
        {
          Version = instance.Version,
          Name = instance.GetType().Name
        };
        foreach (var item in instance.ReadParts(this))
        {
          yield return item;
        }
        yield return new ObjectFooter();
      }

      public T Property<T>(IUnformattedReader reader)
      {
        var holder = reader.UnformattedRead<PropertyHolder>();
        if (holder.CanRead)
        {
          var readable = Activator.CreateInstance(typeof(T)) as ICanRead;
          readable.ReadFrom(reader);
          return (T)readable;
        }
        else
        {
          return (T)holder.Value;
        }
      }

      public IEnumerable<T> List<T>(IUnformattedReader reader)
      {
        var holder = reader.UnformattedRead<ListHolder>();
        for (int count = 0; count < holder.Length; count++)
        {
          var itemHolder = reader.UnformattedRead<ListItemHolder>();
          if (itemHolder.CanRead)
          {
            var readable = Activator.CreateInstance(typeof(T)) as ICanRead;
            readable.ReadFrom(reader);
            yield return (T)readable;
          }
          else
          {
            yield return (T)itemHolder.Value;
          }
        }
      }

      public void Read(IUnformattedReader inner, Action<int> content)
      {
        int version = inner.UnformattedRead<int>();
        if (version == Version)
        {
          var header = inner.UnformattedRead<ObjectHeader>();
          content(header.Version);
          inner.UnformattedRead<ObjectFooter>();
        }
      }

      [Serializable]
      private struct PropertyHolder
      {
        public string Name { get; set; }
        public bool CanRead { get; set; }
        public object Value { get; set; }
      }

      [Serializable]
      private struct ListHolder
      {
        public int Length { get; set; }
        public string Name { get; set; }
      }

      [Serializable]
      private struct ListItemHolder
      {
        public bool CanRead { get; set; }
        public object Value { get; set; }
      }

      [Serializable]
      private struct ObjectHeader
      {
        public int Version { get; set; }
        public string Name { get; set; }
      }

      [Serializable]
      private struct ObjectFooter
      {
        public override string ToString()
        {
          return "footer";
        }
      }

    }

    private class ContiniousReader : IReader, IUnformattedReader
    {
      Stack<IEnumerator> Readers { get; set; }
      IFormatterStrategy ReadFormatter { get; set; }

      public ContiniousReader(ICanRead readable, IFormatterStrategy readFormatter)
      {
        ReadFormatter = readFormatter;
        Readers = new Stack<IEnumerator>();
        Readers.Push(readFormatter.ReadFormatted(readable).GetEnumerator());
      }

      public T UnformattedRead<T>()
      {
        var reader = Readers.Peek();
        if (!reader.MoveNext())
        {
          Readers.Pop();
          reader = Readers.Peek();
          reader.MoveNext();
        }
        IEnumerable lister = reader.Current as IEnumerable;
        if (lister != null)
        {
          reader = lister.GetEnumerator();
          Readers.Push(reader);
          reader.MoveNext();
        }
        return (T)reader.Current;
      }

      public T Property<T>()
      {
        return ReadFormatter.Property<T>(this);
      }

      public IEnumerable<T> List<T>()
      {
        return ReadFormatter.List<T>(this);
      }

      public void Read(Action<int> content)
      {
        ReadFormatter.Read(this, content);
      }
    }

    private class ObjectTextFormatter : IReadFormatter
    {
      enum ValueTypeEnum
      {
        Value, Property, Header, Footer
      }

      TextWriter Writer { get; set; }
      ValueTypeEnum ValueType { get; set; }
      int Indent { get; set; }

      public ObjectTextFormatter(TextWriter writer, int indent)
      {
        Writer = writer;
        Indent = indent;
      }

      public object Format<T>(string name, T value)
      {
        ValueType = ValueTypeEnum.Property;
        return new PropertyHolder
        {
          Name = name,
          Value = value
        };
      }

      struct PropertyHolder
      {
        public string Name;
        public object Value;
      }

      IEnumerable<object> Read(IAmReadable readable)
      {
        ValueType = ValueTypeEnum.Header;
        yield return readable.GetType().Name;
        foreach (var item in readable.ReadParts(this))
        {
          yield return item;
        }
        ValueType = ValueTypeEnum.Footer;
        yield return null;
      }


      void IndentWrite<T>(T value)
      {
        for (int i = 0; i < Indent; i++)
        {
          Writer.Write("\t");
        }
        Writer.Write(value);
      }

      public void Write(IAmReadable source)
      {
        Write(Read(source));
      }

      private void Write(IEnumerable list)
      {
        foreach (var value in list)
        {
          switch (ValueType)
          {
            case ValueTypeEnum.Header:
              IndentWrite(value);
              Writer.Write(" (object)");
              Writer.WriteLine();
              Indent++;
              break;
            case ValueTypeEnum.Footer:
              Indent--;
              break;
            case ValueTypeEnum.Property:
            default:

              var readable = value as ICanRead;
              if (readable != null)
              {
                Write(Read(readable));
              }
              else
              {
                if (value is PropertyHolder)
                {
                  var property = (PropertyHolder)value;
                  var readableProperty = property.Value as ICanRead;
                  IndentWrite(property.Name + ": ");
                  if (readableProperty != null)
                  {
                    Writer.WriteLine();
                    Indent++;
                    Write(Read(readableProperty));
                    Indent--;
                  }
                  else
                  {
                    var lister = property.Value as ICollection;
                    if (lister != null)
                    {
                      Writer.Write("(list)");
                      Writer.WriteLine();
                      Indent++;
                      Write(lister);
                      Indent--;
                    }
                    else
                    {
                      if (property.Value == null)
                      {
                        Writer.Write("[null]");
                      }
                      else
                      {
                        Writer.Write(property.Value.ToString());
                      }
                      Writer.WriteLine();
                    }
                  }
                }
                else
                {
                  if (value == null)
                  {
                    IndentWrite("(null)");
                  }
                  else
                  {
                    IndentWrite(value.ToString());
                  }
                  Writer.WriteLine();
                }
              }
              break;
          }
          ValueType = ValueTypeEnum.Value;
        }
      }
    }


    private class StreamMemento : IMemento, IUnformattedReader
    {
      Stream InnerStream { get; set; }
      BinaryFormatter Formatter { get; set; }
      IFormatterStrategy ReadFormatter { get; set; }

      public StreamMemento(Stream stream, IFormatterStrategy readFormatter)
      {
        InnerStream = stream;
        Formatter = new BinaryFormatter();
        ReadFormatter = readFormatter;
      }

      public void Reset()
      {
        if (InnerStream.CanSeek)
        {
          InnerStream.Position = 0;
        }
      }

      public void ReadFrom(IAmReadable other)
      {
        ReadFrom(ReadFormatter.ReadFormatted(other));
      }

      public void WriteTo(ICanRead other)
      {
        other.ReadFrom(this);
      }

      private void ReadFrom(IEnumerable list)
      {
        foreach (var value in list)
        {
          IEnumerable lister = value as IEnumerable;
          if (lister != null)
          {
            ReadFrom(lister);
          }
          else
          {
            Formatter.Serialize(InnerStream, value);
          }
        }
      }

      public T UnformattedRead<T>()
      {
        return (T)Formatter.Deserialize(InnerStream);
      }

      public T Property<T>()
      {
        return ReadFormatter.Property<T>(this);
      }

      public IEnumerable<T> List<T>()
      {
        return ReadFormatter.List<T>(this);
      }

      public void Read(Action<int> content)
      {
        ReadFormatter.Read(this, content);
      }

    }
  }


  public interface IReadFormatter
  {
    object Format<T>(string name, T value);
  }

  public interface IFormatterStrategy : IReadFormatter
  {
    IEnumerable<object> ReadFormatted(IAmReadable instance);
    T Property<T>(IUnformattedReader reader);
    IEnumerable<T> List<T>(IUnformattedReader reader);
    void Read(IUnformattedReader inner, Action<int> content);
  }

  public interface IReader
  {
    T Property<T>();
    IEnumerable<T> List<T>();
  }

  public interface IUnformattedReader : IReader
  {
    T UnformattedRead<T>();
    void Read(Action<int> content);
  }

  public interface IMemento
  {
    void Reset();
    void ReadFrom(IAmReadable readable);
    void WriteTo(ICanRead readable);
  }

  public interface IAmReadable
  {
    int Version { get; }
    IEnumerable<object> ReadParts(IReadFormatter formatter);
  }

  public interface ICanRead : IAmReadable
  {
    void ReadFrom(IReader reader, int version);
  }
}
