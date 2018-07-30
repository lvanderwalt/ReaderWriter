using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ApprovalTests;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ReaderWriter.Test
{
  [TestClass]
  public class ReaderWriterExtensionTests
  {

    [TestMethod]
    public void ReadAndWrite()
    {
      var example = new ExampleClass { StringValue = "StringValue" };
      example.Values.Add("value1");

      var stream = new MemoryStream();
      example.WriteTo(stream);

      var newInstance = new ExampleClass();
      stream.Position = 0;
      newInstance.ReadFrom(stream);

      Assert.AreEqual(example.StringValue, newInstance.StringValue);
      Assert.AreEqual(example.Values.Count, newInstance.Values.Count);
      Assert.AreEqual(example.Values[0], newInstance.Values[0]);

    }

    [TestMethod]
    public void CopyingAnObject()
    {
      var source = NewTestObject();

      var clone1 = new TestOwner { Name = "wrong" };

      source.CloneTo(clone1);

      var clone2 = new TestOwner { Name = "wrong" };
      var memento = source.GetMemento();
      memento.Reset();
      memento.WriteTo(clone2);

      var sourceStr = source.AsString();
      var cloneStr = clone1.AsString();
      var mementoStr = clone2.AsString();
      Assert.IsTrue(cloneStr == sourceStr && mementoStr == sourceStr);

      //verifying with file: ReaderWriterExtensionTests.CopyingAnObject.approved.txt
      Approvals.Verify($@"
*original:*
{sourceStr}

*after cloning:*
{cloneStr}

*after using memento:*
{mementoStr}
");

    }

    [TestMethod]
    public void SaveObject_ThenLoadIntoNewVersionWithAddedProperty()
    {
      var versionChange = SimulateVersionChange<TestOwner, TestOwnerWithNewProperty>(
          NewTestObject(), (newVersion) =>
          {
            newVersion.NewProp = "the new property";
          });

      //verifying with file: ReaderWriterExtensionTests.SaveObject_ThenLoadIntoNewVersionWithAddedProperty.approved.txt
      Approvals.Verify($@"
*source:*
{versionChange.OldVersion}

*after new version read from old source:*
{versionChange.AfterNewVersionRead}

*after new version save and load:*
{versionChange.AfterNewVersionRountrip}
");
    }


    [TestMethod]
    public void SaveObject_ThenLoadIntoNewVersionWithAddedPropertyOnChildObject()
    {
      var versionChange = SimulateVersionChange<TestOwner, TestOwnerWithNewPropertyOnChild>(
          NewTestObject(), (newVersion) =>
          {
            newVersion.Inner.NewProp = "the new property";
            foreach (var item in newVersion.Inners)
            {
              item.NewProp = "new prop";
            }
          });

      //verifying with file: ReaderWriterExtensionTests.SaveObject_ThenLoadIntoNewVersionWithAddedPropertyOnChildObject.approved.txt
      Approvals.Verify($@"
*source:*
{versionChange.OldVersion}

*after new version read from old source:*
{versionChange.AfterNewVersionRead}

*after new version save and load:*
{versionChange.AfterNewVersionRountrip}
");
    }

    [TestMethod]
    public void SaveObject_ThenLoadIntoNewVersionWithRemovedProperty()
    {
      var versionChange = SimulateVersionChange<TestOwner, TestOwnerWithPropertyRemoved>(
          NewTestObject(), null);

      //verifying with file: ReaderWriterExtensionTests.SaveObject_ThenLoadIntoNewVersionWithRemovedProperty.approved.txt
      Approvals.Verify($@"
*source:*
{versionChange.OldVersion}

*after new version read from old source:*
{versionChange.AfterNewVersionRead}

*after new version save and load:*
{versionChange.AfterNewVersionRountrip}
");
    }

    [TestMethod]
    public void SaveObject_ThenLoadIntoNewVersionWithPropertyTypeChanged()
    {
      var versionChange = SimulateVersionChange<TestOwner, TestOwnerWithPropertyChange>(
          NewTestObject(), (newVersion) =>
          {
            newVersion.ChangedProp = 1212;
          });

      //verifying with file: ReaderWriterExtensionTests.SaveObject_ThenLoadIntoNewVersionWithPropertyTypeChanged.approved.txt
      Approvals.Verify($@"
*source:*
{versionChange.OldVersion}

*after new version read from old source:*
{versionChange.AfterNewVersionRead}

*after new version save and load:*
{versionChange.AfterNewVersionRountrip}
");
    }


    //[TestMethod] //this is a performance test
    public void SpeedOfByteArrayConversion()
    {
      var source = new TestOwner
      {
        Inners = new List<CopyTestChild>()
      };
      var count = 5000;
      for (int i = 0; i < count; i++)
      {
        source.Inners.Add(new CopyTestChild
        {
          Name = i.ToString()
        });
      }

      Stopwatch watch = new Stopwatch();
      watch.Start();
      var byteArray = source.AsByteArray();
      watch.Stop();
      var elapsed = watch.Elapsed;
      Console.WriteLine();
      Console.WriteLine($"copying {count} items into byte array: {elapsed}");
      Console.WriteLine();


      watch = new Stopwatch();
      source = new TestOwner();
      watch.Start();
      source.ReadFrom(byteArray);
      watch.Stop();
      elapsed = watch.Elapsed;
      Console.WriteLine();
      Console.WriteLine($"copying {count} items out of byte array: {elapsed}");
      Console.WriteLine();
    }


    #region helpers

    struct VersionChangeResult
    {
      public string OldVersion;
      public string AfterNewVersionRead;
      public string AfterNewVersionRountrip;
    }

    private static VersionChangeResult SimulateVersionChange<TOld, TNew>(TOld oldVersion,
        Action<TNew> onAfterReadFromOldVersion)
        where TOld : ICanRead
        where TNew : ICanRead, new()
    {
      var result = new VersionChangeResult
      {
        OldVersion = oldVersion.AsString()
      };

      var stream = new MemoryStream();
      oldVersion.WriteTo(stream);

      var newVersion = new TNew();
      stream.Position = 0;
      newVersion.ReadFrom(stream);
      result.AfterNewVersionRead = newVersion.AsString();

      if (onAfterReadFromOldVersion != null)
      {
        onAfterReadFromOldVersion(newVersion);
      }
      stream.Position = 0;
      newVersion.WriteTo(stream);

      newVersion = new TNew();
      stream.Position = 0;
      newVersion.ReadFrom(stream);
      result.AfterNewVersionRountrip = newVersion.AsString();

      return result;
    }



    private static TestOwner NewTestObject()
    {
      var source = new TestOwner
      {
        Name = "source",
        Inner = new CopyTestChild
        {
          Name = "inner property"
        },
        Inners = new List<CopyTestChild>
        {
            new CopyTestChild { Name = "inner list item1" },
            new CopyTestChild { Name = "inner list item2" }
        }
      };
      return source;
    }

    class TestOwner : ICanRead
    {
      public int Version { get { return 1; } }
      public string Name { get; set; }
      public List<CopyTestChild> Inners { get; set; }
      public CopyTestChild Inner { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
        yield return formatter.Format("Inner", Inner);
        yield return formatter.Format("Inners", Inners);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
        Inner = reader.Property<CopyTestChild>();
        Inners = reader.List<CopyTestChild>().ToList();
      }

    }

    class TestOwnerWithPropertyChange : ICanRead
    {
      public int Version { get { return 2; } }

      //changed from string to int:
      public int ChangedProp { get; set; }

      public List<CopyTestChild> Inners { get; set; }
      public CopyTestChild Inner { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("ChangedProp", ChangedProp);
        yield return formatter.Format("Inner", Inner);
        yield return formatter.Format("Inners", Inners);
      }

      public void ReadFrom(IReader reader, int version)
      {
        if (version < 2)
        {
          var oldProperty = reader.Property<string>();
          int converted;
          if (int.TryParse(oldProperty, out converted))
          {
            ChangedProp = converted;
          }
        }
        else
        {
          ChangedProp = reader.Property<int>();
        }

        Inner = reader.Property<CopyTestChild>();
        Inners = reader.List<CopyTestChild>().ToList();
      }

    }

    class TestOwnerWithPropertyRemoved : ICanRead
    {
      public int Version { get { return 2; } }
      public string Name { get; set; }
      public List<CopyTestChild> Inners { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
        yield return formatter.Format("Inners", Inners);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
        if (version < 2)
        {
          var oldProperty = reader.Property<CopyTestChild>();
        }
        Inners = reader.List<CopyTestChild>().ToList();
      }

    }

    class TestOwnerWithNewPropertyOnChild : ICanRead
    {
      public int Version { get { return 1; } }
      public string Name { get; set; }
      public List<CopyTestChildWithNewProperty> Inners { get; set; }
      public CopyTestChildWithNewProperty Inner { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
        yield return formatter.Format("Inner", Inner);
        yield return formatter.Format("Inners", Inners);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
        Inner = reader.Property<CopyTestChildWithNewProperty>();
        Inners = reader.List<CopyTestChildWithNewProperty>().ToList();
      }

    }

    class TestOwnerWithNewProperty : ICanRead
    {
      public int Version { get { return 2; } }
      public string Name { get; set; }
      public List<CopyTestChild> Inners { get; set; }
      public CopyTestChild Inner { get; set; }
      public string NewProp { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
        yield return formatter.Format("Inner", Inner);
        yield return formatter.Format("Inners", Inners);
        yield return formatter.Format("NewProp", NewProp);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
        Inner = reader.Property<CopyTestChild>();
        Inners = reader.List<CopyTestChild>().ToList();
        if (version >= 2)
        {
          NewProp = reader.Property<string>();
        }
      }

    }

    class CopyTestChild : ICanRead
    {
      public int Version { get { return 1; } }
      public string Name { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
      }
    }

    class CopyTestChildWithNewProperty : ICanRead
    {
      public int Version { get { return 2; } }
      public string Name { get; set; }
      public string NewProp { get; set; }

      public IEnumerable<object> ReadParts(IReadFormatter formatter)
      {
        yield return formatter.Format("Name", Name);
        yield return formatter.Format("NewProp", NewProp);
      }

      public void ReadFrom(IReader reader, int version)
      {
        Name = reader.Property<string>();
        if (version >= 2)
        {
          NewProp = reader.Property<string>();
        }
      }
    }

    #endregion
  }
}
