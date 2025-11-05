using Bogus;
using MongoDB.Bson;

namespace UvA.Workflow.Tests;

public class PropertyBuilder
{
    private readonly Faker faker = new();
    
    public BsonValue Value(BsonValue value) => value;
    
    public BsonDocument Person(string? displayName = null, string? email = null, string? externalId = null, string? objectId = null )
    => new()
    {
            { "_id", NewObjectId(objectId) },
            { "ExternalId", externalId ?? faker.Random.Int().ToString() },
            { "DisplayName", displayName ?? faker.Name.FullName() },
            { "Email", email ?? faker.Internet.Email() }
        };
    
    public BsonDocument FileRef(string? name = null, string? objectId = null)
        => new()
        {
            { "_id", NewObjectId(objectId) },
            { "Name", name ?? faker.Lorem.Word() }
        };
    
    public BsonArray Array(params BsonValue[] items) => new(items);
    
    public BsonArray Array(params Func<PropertyBuilder,BsonValue>[] items)
    {
        var array = new BsonArray();
        foreach (var builder in items)
        {
            var propertyBuilder = new PropertyBuilder();
            array.Add(builder(propertyBuilder));
        }
        return array;
    }
    
    public BsonArray Array(int size, Func<PropertyBuilder,int, BsonValue> builder)
    {
        var array = new BsonArray();
        for (var i = 0; i < size; i++)
        {
            var propertyBuilder = new PropertyBuilder();
            array.Add(builder(propertyBuilder,i));
        }
        return array;
    }
    
    public BsonArray Array(int size, Func<PropertyBuilder, BsonValue> builder)
    {
        var array = new BsonArray();
        for (var i = 0; i < size; i++)
        {
            var propertyBuilder = new PropertyBuilder();
            array.Add(builder(propertyBuilder));
        }
        return array;
    }

    private ObjectId NewObjectId(string? objectId)
    {
        return string.IsNullOrEmpty(objectId) ? ObjectId.GenerateNewId() : new ObjectId(objectId);
    }
}