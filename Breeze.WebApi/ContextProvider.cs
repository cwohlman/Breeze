﻿using System;
using System.Collections.Generic;
using System.ComponentModel;

using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Breeze.WebApi {
  // Base for EFContextProvider
  public abstract class ContextProvider {

    public IKeyGenerator KeyGenerator { get; set; }

    public string Metadata() {
      lock (_metadataLock) {
        if (_jsonMetadata == null) {
          _jsonMetadata = BuildJsonMetadata();
        }

        return _jsonMetadata;
      }
    }
    /// <summary>
    /// Override to use a specialized JsonSerializer implementation.
    /// </summary>
    protected virtual JsonSerializer CreateSerializer() {
        return new JsonSerializer();
    }

    public SaveResult SaveChanges(JObject saveBundle) {
      EntitiesWithAutoGeneratedKeys = new List<EntityInfo>();
      var dynSaveBundle = (dynamic)saveBundle;
      var entitiesArray = (JArray)dynSaveBundle.entities;
      var saveOptions = dynSaveBundle.saveOptions;
      var jsonSerializer = CreateSerializer();
      var jObjects = entitiesArray.Select(jt => (dynamic)jt).ToList();

      var groups = jObjects.GroupBy(jo => (String)jo.entityAspect.entityTypeName).ToList();
      var saveMap = new Dictionary<Type, List<EntityInfo>>();
      groups.ForEach(g => {
        var entityType = LookupEntityType(g.Key);
        var entityInfos = g.Select(jo => (EntityInfo)CreateEntityInfoFromJson(jo, entityType, jsonSerializer))
          .Where(ei => BeforeSaveEntity(ei))
          .ToList();
        saveMap.Add(entityType, entityInfos);
      });
      saveMap = BeforeSaveEntities(saveMap);
      
      var keyMappings = SaveChangesCore(saveMap);
      
      var entities = saveMap.SelectMany(kvp => kvp.Value.Select(entityInfo => entityInfo.Entity)).ToList();

      return new SaveResult() { Entities = entities, KeyMappings = keyMappings };
    }

    #region abstract and virtual methods 

    protected abstract String BuildJsonMetadata();
    
    protected abstract List<KeyMapping> SaveChangesCore(Dictionary<Type, List<EntityInfo>> saveMap);

    protected virtual EntityInfo CreateEntityInfo() {
      return new EntityInfo();
    }
    
    /// <summary>
    /// The method is called for each entity to be saved before the save occurs.  If this method returns 'false'
    /// then the entity will be excluded from the save. There is no need to call the base implementation of this
    /// method when overriding it. 
    /// </summary>
    /// <param name="entityInfo"></param>
    /// <returns></returns>
    protected virtual bool BeforeSaveEntity(EntityInfo entityInfo) {
      return true;
    }

    protected virtual Dictionary<Type, List<EntityInfo>> BeforeSaveEntities(Dictionary<Type, List<EntityInfo>> saveMap) {
      return saveMap;
    }

    #endregion
    
    protected EntityInfo CreateEntityInfoFromJson(dynamic jo, Type entityType, JsonSerializer jsonSerializer) {
      var entityInfo = CreateEntityInfo();
      entityInfo.Entity = jsonSerializer.Deserialize(new JTokenReader(jo), entityType);
      entityInfo.EntityState = (EntityState)Enum.Parse(typeof(EntityState), (String)jo.entityAspect.entityState);

      var jprops = ((System.Collections.IEnumerable)jo.entityAspect.originalValuesMap).Cast<JProperty>();
      entityInfo.OriginalValuesMap = jprops.ToDictionary(jprop => jprop.Name, jprop => {
        var val = jprop.Value as JValue;
        if (val != null) {
          return val.Value;
        } else {
          return jprop.Value as JObject;
        }
      });
      var autoGeneratedKey = jo.entityAspect.autoGeneratedKey;
      if (entityInfo.EntityState == EntityState.Added && autoGeneratedKey != null) {
        entityInfo.AutoGeneratedKey = new AutoGeneratedKey(entityInfo.Entity, autoGeneratedKey);
        EntitiesWithAutoGeneratedKeys.Add(entityInfo);
      }
      return entityInfo;
    }

    protected List<EntityInfo> EntitiesWithAutoGeneratedKeys { get; set; }
    
    protected Type LookupEntityType(String entityTypeName) {
      var delims = new string[] { ":#" };
      var parts = entityTypeName.Split(delims, StringSplitOptions.None);
      var shortName = parts[0];
      var ns = parts[1];

      var typeName = ns + "." + shortName;
      var type = ProbeAssemblies.Value
        .Select(a => a.GetType(typeName, false, true))
        .FirstOrDefault(t => t != null);
      if (type!=null) {
        return type;
      } else {
        throw new ArgumentException("Assembly could not be found for " + entityTypeName);
      }
    }

    protected static Lazy<Type> KeyGeneratorType = new Lazy<Type>( () => {
       var typeCandidates = ProbeAssemblies.Value.Concat( new Assembly[] {typeof(IKeyGenerator).Assembly})
        .SelectMany(a => a.GetTypes()).ToList();
      var generatorTypes = typeCandidates.Where(t => typeof (IKeyGenerator).IsAssignableFrom(t) && !t.IsAbstract)
        .ToList();
      if (generatorTypes.Count == 0) {
        throw new Exception("Unable to locate a KeyGenerator implementation.");
      }
      return generatorTypes.First();
    });

    // Cache the ProbeAssemblies.
    // Note: look at BuildManager.GetReferencedAssemblies if we start having
    // issues with Assemblies not yet having been loaded.
    protected static Lazy<List<Assembly>> ProbeAssemblies = new Lazy<List<Assembly>>( () => 
      AppDomain.CurrentDomain.GetAssemblies().Where(a => !IsFrameworkAssembly(a)).ToList()
    );

    protected static bool IsFrameworkAssembly(Assembly assembly) {
      var attrs = assembly.GetCustomAttributes(typeof(AssemblyProductAttribute), false).OfType<AssemblyProductAttribute>();
      var attr = attrs.FirstOrDefault();
      if (attr == null) {
        return false;
      }
      var productName = attr.Product;
      return FrameworkProductNames.Any(nm => productName.StartsWith(nm));
    }

    protected static readonly List<String> FrameworkProductNames = new List<String> {
      "Microsoft® .NET Framework",
      "Microsoft (R) Visual Studio (R) 2010",
      "Microsoft ASP.",
      "System.Net.Http",
      "Json.NET",
      "Irony",
      "Breeze.WebApi"
    };
  

    private object _metadataLock = new object();
    private string _jsonMetadata;
    
  }

  public interface IKeyGenerator {
    void UpdateKeys(List<TempKeyInfo> keys);
  }

  // instances of this sent to KeyGenerator
  public class TempKeyInfo {
    public TempKeyInfo(EntityInfo entityInfo) {
      _entityInfo = entityInfo;
    }
    public Object Entity {
      get { return _entityInfo.Entity; }
    }
    public Object TempValue {
      get { return _entityInfo.AutoGeneratedKey.TempValue; }
    }
    public Object RealValue {
      get { return _entityInfo.AutoGeneratedKey.RealValue; }
      set { _entityInfo.AutoGeneratedKey.RealValue = value; }
    }

    public PropertyInfo Property {
      get { return _entityInfo.AutoGeneratedKey.Property; }
    }

    private EntityInfo _entityInfo;

  }

  [Flags]
  public enum EntityState {
    Detached = 1,
    Unchanged = 2,
    Added = 4,
    Deleted = 8,
    Modified = 16,
  }

  public class EntityInfo {
    internal EntityInfo() {
    }

    public Object Entity { get; internal set; }
    public EntityState EntityState { get; internal set; }
    public Dictionary<String, Object> OriginalValuesMap { get; internal set; }
    internal AutoGeneratedKey AutoGeneratedKey;
  }

  public enum AutoGeneratedKeyType {
    None,
    Identity,
    KeyGenerator
  }

  public class AutoGeneratedKey {
    public AutoGeneratedKey(Object entity, dynamic autoGeneratedKey) {
      Entity = entity;
      PropertyName = autoGeneratedKey.propertyName;
      AutoGeneratedKeyType = (AutoGeneratedKeyType)Enum.Parse(typeof(AutoGeneratedKeyType), (String)autoGeneratedKey.autoGeneratedKeyType);
      // TempValue and RealValue will be set later. - TempValue during Add, RealValue after save completes.
    }

    public Object Entity;
    public AutoGeneratedKeyType AutoGeneratedKeyType;
    public String PropertyName;
    public PropertyInfo Property {
      get {
        if (_property == null) {
          _property = Entity.GetType().GetProperty(PropertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        return _property;
      }
    }
    public Object TempValue;
    public Object RealValue;
    private PropertyInfo _property;
  }

  // Types returned to javascript as Json.
  public class SaveResult {
    public List<Object> Entities;
    public List<KeyMapping> KeyMappings;
    public String Error;
  }

  public class KeyMapping {
    public String EntityTypeName;
    public Object TempValue;
    public Object RealValue;
  }

  public class JsonPropertyFixupWriter : JsonTextWriter {
    public JsonPropertyFixupWriter(TextWriter textWriter)
      : base(textWriter) {
      _isName = false;
    }

    public override void WritePropertyName(string name) {
      if (name.StartsWith("@")) {
        name = name.Substring(1);
      }
      name = ToCamelCase(name);
      _isName = name == "type";
      base.WritePropertyName(name);
    }

    public override void WriteValue(string value) {
      if (_isName) {
        base.WriteValue("Edm." + value);
      } else {
        base.WriteValue(value);
      }
    }

    private static string ToCamelCase(string s) {
      if (string.IsNullOrEmpty(s) || !char.IsUpper(s[0]))
        return s;
      string str = char.ToLower(s[0], CultureInfo.InvariantCulture).ToString((IFormatProvider)CultureInfo.InvariantCulture);
      if (s.Length > 1)
        str = str + s.Substring(1);
      return str;
    }

    private bool _isName;

  }
 
}