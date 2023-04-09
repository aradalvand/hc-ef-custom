using System.Linq.Expressions;
using System.Reflection;
using AgileObjects.ReadableExpressions;

public static class InterfaceTypeDescriptorExtensions
{
	public static Test1I<TDto> Mapped<TDto>(this IInterfaceTypeDescriptor<TDto> descriptor) where TDto : BaseDto
	{
		return new(descriptor);
	}
}

public class Test1I<TDto> where TDto : BaseDto
{
	private readonly IInterfaceTypeDescriptor<TDto> _descriptor;

	public Test1I(IInterfaceTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public void To<TEntity>(Action<MappingDescriptor<TDto, TEntity>>? configure = null)
	{
		// configure?.Invoke(new(_descriptor));

		_descriptor.Ignore(d => d._Meta); // NOTE: We do our configuration (such as ignoring the meta property) after the user code, because we want it to take precedence.

		_descriptor.Extend().OnBeforeCreate((c, d) =>
		{
			Console.WriteLine($"OnBeforeCreate: {typeof(TDto).Name}");
			TypeMapping.Dictionary.Add(typeof(TEntity), typeof(TDto));
			TypeMapping.Dictionary.Add(typeof(TDto), typeof(TEntity));
		});

		_descriptor.Extend().OnBeforeCompletion((c, d) =>
		{
			Console.WriteLine("---");
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"OnBeforeCompletion: {typeof(TDto).Name}");
			Console.ResetColor();
			foreach (var field in d.Fields) // NOTE: We examine the type's fields right before the configuration is all done so that we operate upon exactly the fields that are going to be part of the type in the schema. The user might have removed (ignored) or added fields before this.
			{
				Console.WriteLine("--");
				Console.WriteLine($"Field: {field}");
				if (field.Member is null)
					throw new InvalidOperationException("All fields in a mapped type must correspond to a property on the DTO type.");  // NOTE: This prevents the user from creating arbitrary new fields (e.g. `descriptor.Field("FooBar")`).

				Console.WriteLine($"field.ContextData.Count: {field.ContextData.Count}");
				var fieldData = field.ContextData.GetValueOrDefault(WellKnownContextKeys.MappedFieldData) as MappedFieldData;
				if (fieldData?.Expression is not null)
					continue;

				var dtoProp = (PropertyInfo)field.Member; // NOTE: We assume the member behind the field is a property (and this assumption in practically safe in our case, although not safe in principle, if you will)
				var namesakeEntityProp = typeof(TEntity).GetProperty(dtoProp.Name); // NOTE: Property on the entity type with the same name.

				if (
					namesakeEntityProp is null ||
					!AreAssignable(dtoProp.PropertyType, namesakeEntityProp.PropertyType)
				)
					throw new InvalidOperationException($"Property '{dtoProp.Name}' on the DTO type '{typeof(TDto)}' was not configured explicitly and no implicitly matching property with the same name and type on the entity type was found..");

				// NOTE: Doing this here as opposed to in the projection middleware has two advantages: 1. No reflection at runtime (only on startup) 2. If no matching entity property exists we throw on startup instead of at runtime.
				var param = Expression.Parameter(typeof(TEntity));
				var body = Expression.Property(param, namesakeEntityProp);
				var expression = Expression.Lambda(body, param);
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"{dtoProp.DeclaringType.Name}.{dtoProp.Name} = {body.ToReadableString()}");
				Console.ResetColor();
				// TODO: Far too much work:
				if (fieldData is null)
					field.ContextData[WellKnownContextKeys.MappedFieldData] = new MappedFieldData(expression, false);
				else
					field.ContextData[WellKnownContextKeys.MappedFieldData] = fieldData with
					{
						Expression = expression
					};

				static bool AreAssignable(Type dtoProp, Type entityProp)
				{
					// NOTE: We check "assignability" and not equality because the entity prop might be, for example, ICollection while
					if (dtoProp.IsAssignableFrom(entityProp)) // NOTE: Simple cases like where the types are directly assignable
						return true;

					// TODO: Improve
					if (
						entityProp.IsAssignableTo(typeof(IEnumerable<object>)) &&
						dtoProp.IsAssignableTo(typeof(IEnumerable<object>))
					)
					{
						entityProp = entityProp.GetGenericArguments().First();
						dtoProp = dtoProp.GetGenericArguments().First();
					}
					var entityPropDtoType = TypeMapping.Dictionary.GetValueOrDefault(entityProp);
					if (entityPropDtoType is not null && dtoProp.IsAssignableFrom(entityPropDtoType))
						return true;

					return false;
				}
			}
		});
	}
}
