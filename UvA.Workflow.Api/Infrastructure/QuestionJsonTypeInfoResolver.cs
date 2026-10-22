using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using UvA.Workflow.Api.Submissions.Dtos;

namespace UvA.Workflow.Api.Infrastructure;

public sealed class QuestionJsonTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    private static readonly Type[] QuestionTypes = typeof(QuestionDto).Assembly.GetTypes()
        .Where(type => type.IsSubclassOf(typeof(QuestionDto)) && !type.IsAbstract)
        .ToArray();

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var info = base.GetTypeInfo(type, options);
        if (type == typeof(QuestionDto))
        {
            info.PolymorphismOptions = new JsonPolymorphismOptions();
            foreach (var questionType in QuestionTypes)
                info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(questionType));
        }

        return info;
    }
}