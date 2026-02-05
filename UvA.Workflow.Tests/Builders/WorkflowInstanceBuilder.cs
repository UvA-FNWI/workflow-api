using MongoDB.Bson;
using UvA.Workflow.Events;
using UvA.Workflow.WorkflowInstances;

namespace UvA.Workflow.Tests;

public class WorkflowInstanceBuilder
{
    private string? id;
    private string? workflowDefinition;
    private string? currentStep;
    private string? parentId;

    private readonly Dictionary<string, BsonValue> properties = new();
    private readonly Dictionary<string, InstanceEvent> events = new();

    public WorkflowInstanceBuilder With(string? workflowDefinition = null, string? currentStep = null,
        string? parentId = null,
        string? id = null)
    {
        this.id = id;
        this.workflowDefinition = workflowDefinition;
        this.currentStep = currentStep;
        this.parentId = parentId;
        return this;
    }

    // Core fields
    public WorkflowInstanceBuilder WithId(string objectId)
    {
        id = objectId;
        return this;
    }

    public WorkflowInstanceBuilder WithWorkflowDefinition(string workflowDefinition)
    {
        this.workflowDefinition = workflowDefinition;
        return this;
    }

    public WorkflowInstanceBuilder WithCurrentStep(string currentStep)
    {
        this.currentStep = currentStep;
        return this;
    }

    public WorkflowInstanceBuilder WithParentId(string? parentId)
    {
        this.parentId = parentId;
        return this;
    }

    public WorkflowInstanceBuilder WithEvents(params Func<EventBuilder, EventBuilder>[] builders)
    {
        foreach (var evBuilder in builders)
        {
            var eventBuilder = new EventBuilder();
            var instanceEvent = evBuilder(eventBuilder).Build();
            events[instanceEvent.Id] = instanceEvent;
        }

        return this;
    }

    public WorkflowInstanceBuilder WithEvent(string eventId, DateTime? date = null)
    {
        events[eventId] = new InstanceEvent
        {
            Id = eventId,
            Date = date
        };
        return this;
    }

    public WorkflowInstanceBuilder WithProperties(
        params (string name, Func<PropertyBuilder, BsonValue> builder)[] props)
    {
        foreach (var (name, builder) in props)
        {
            var propertyBuilder = new PropertyBuilder();
            properties[name] = builder(propertyBuilder);
        }

        return this;
    }


    public WorkflowInstance Build()
    {
        return new WorkflowInstance
        {
            Id = id ?? ObjectId.GenerateNewId().ToString()!,
            WorkflowDefinition = workflowDefinition ??
                                 throw new InvalidOperationException("WorkflowDefinition must be specified"),
            CurrentStep = currentStep ?? throw new InvalidOperationException("CurrentStep must be specified"),
            ParentId = parentId,
            Properties = new Dictionary<string, BsonValue>(properties),
            Events = new Dictionary<string, InstanceEvent>(events)
        };
    }
}