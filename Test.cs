using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Execution.Processing;

namespace hc_ef_custom;

public static class WellKnownContextKeys
{
	public const string MappedTypeData = nameof(MappedTypeData);
	public const string MappedFieldData = nameof(MappedFieldData);
}

public record MappedTypeData(
	Type CorrespondingEntityType
);

public record MappedFieldData(
	LambdaExpression? Expression = null,
	bool? CheckForNull = null
);

public static class ObjectTypeDescriptorExtensions
{
	public static Test1<TDto> Mapped<TDto>(this IObjectTypeDescriptor<TDto> descriptor) where TDto : BaseDto // TODO: One for interface types
	{
		return new(descriptor);
	}
}

public class Test1<TDto> where TDto : BaseDto
{
	private readonly IObjectTypeDescriptor<TDto> _descriptor;

	public Test1(IObjectTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public void To<TEntity>(Action<MappingDescriptor<TDto, TEntity>>? configure = null)
	{
		configure?.Invoke(new(_descriptor));

		_descriptor.Ignore(d => d._Meta); // NOTE: We do our configuration (such as ignoring the meta property) after the user code, because we want it to take precedence.

		_descriptor.Extend().OnBeforeCreate(d =>
		{
			d.ContextData[WellKnownContextKeys.MappedTypeData] = new MappedTypeData(
				CorrespondingEntityType: typeof(TEntity)
			);
		});
		_descriptor.Extend().OnBeforeCompletion((_, d) =>
		{
			foreach (var field in d.Fields) // NOTE: We examine the type's fields right before the configuration is all done so that we operate upon exactly the fields that are going to be part of the type in the schema. The user might have removed (ignored) or added fields before this.
			{
				if (field.IsIntrospectionField)
					continue;

				if (field.Member is null)
					throw new InvalidOperationException("All fields in a mapped type must correspond to a property on the DTO type.");  // NOTE: This prevents the user from creating arbitrary new fields (e.g. `descriptor.Field("FooBar")`).

				var fieldData = field.ContextData.GetValueOrDefault(WellKnownContextKeys.MappedFieldData) as MappedFieldData;
				if (fieldData?.Expression is not null)
					continue;

				var dtoProp = (PropertyInfo)field.Member; // NOTE: We assume the member behind the field is a property (and this assumption in practically safe in our case, although not safe in principle, if you will)
				var namesakeEntityProp = typeof(TEntity).GetProperty(dtoProp.Name); // NOTE: Property on the entity type with the same name and type.
				Type foo = null;
				bool matches =
					namesakeEntityProp is not null &&
					(namesakeEntityProp.PropertyType.IsAssignableFrom(dtoProp.PropertyType) || // NOTE: We check "assignability" and not equality because the entity prop might be, for example, ICollection while
					namesakeEntityProp.PropertyType.IsAssignableFrom(foo));
				if (!matches)
					throw new InvalidOperationException($"Property '{dtoProp.Name}' on the DTO type '{typeof(TDto)}' was not configured explicitly and no implicitly matching property with the same name and type on the entity type was found..");

				// NOTE: Doing this here as opposed to in the projection middleware has two advantages: 1. No reflection at runtime (only on startup) 2. If no matching entity property exists we throw on startup instead of at runtime.
				var param = Expression.Parameter(typeof(TEntity));
				var body = Expression.Property(param, namesakeEntityProp);
				var expression = Expression.Lambda(body, param);
				// TODO: Far too much work:
				if (fieldData is null)
					field.ContextData[WellKnownContextKeys.MappedFieldData] = new MappedFieldData(expression);
				else
					field.ContextData[WellKnownContextKeys.MappedFieldData] = fieldData with
					{
						Expression = expression
					};
			}
		});
	}
}

public class MappingDescriptor<TDto, TEntity>
{
	private readonly IObjectTypeDescriptor<TDto> _descriptor;

	public MappingDescriptor(IObjectTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> Property<TProperty>(
		Expression<Func<TDto, TProperty?>> propertySelector
	)
	{
		return new(_descriptor.Field(propertySelector));
	}
}

public class PropertyMappingDescriptor<TDto, TEntity, TProperty>
{
	private readonly IObjectFieldDescriptor _descriptor;

	public PropertyMappingDescriptor(IObjectFieldDescriptor descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> MapTo(
		Expression<Func<TEntity, TProperty>> map
	)
	{
		_descriptor.Extend().OnBeforeCreate(d =>
		{
			var fieldData = d.ContextData.GetValueOrDefault(WellKnownContextKeys.MappedFieldData) as MappedFieldData;
			// TODO: Far too much work:
			if (fieldData is null)
				d.ContextData[WellKnownContextKeys.MappedFieldData] = new MappedFieldData(map);
			else
				d.ContextData[WellKnownContextKeys.MappedFieldData] = fieldData with
				{
					Expression = map
				};
		});
		return this;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> UseAuth(
		Action<PropertyAuthMappingDescriptor<TDto, TEntity, TProperty>> configure
	)
	{
		_descriptor.Extend().OnBeforeCreate(d => // todo
		{
			// d.ContextData[WellKnownContextKeys.UseAuth] = true;
		});
		configure(new(_descriptor));
		return this;
	}
}

public class PropertyAuthMappingDescriptor<TDto, TEntity, TProperty>
{
	private readonly IObjectFieldDescriptor _descriptor;

	public PropertyAuthMappingDescriptor(IObjectFieldDescriptor descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustBeAuthenticated()
	{
		return this;
	}

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustHaveRole()
	{
		return this;
	}

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> MustNotHaveRule()
	{
		return this;
	}

	public PropertyAuthMappingDescriptor<TDto, TEntity, TProperty> Must(
		Func<AuthenticatedUser, Expression<Func<TEntity, bool>>> ruleExpression
	)
	{
		// string key = Guid.NewGuid().ToString("N"); // Does this work?
		// AuthRule rule = new(key, ruleExpression);

		// var definition = _descriptor.Extend().Definition;
		// if (definition.ContextData.GetValueOrDefault(WellKnownContextKeys.MappedFieldData) is List<AuthRule> authRules)
		// 	authRules.Add(rule);
		// else
		// 	definition.ContextData[WellKnownContextKeys.MappedFieldData] = new List<AuthRule> { rule };

		// _descriptor.Use(next => async context =>
		// {
		// 	await next(context);
		// 	var result = context.Parent<CourseDto>()._Meta[key];
		// 	if (result)
		// 		Console.WriteLine("Permitted.");
		// 	else
		// 		Console.WriteLine("Not permitted.");
		// });

		return this;
	}
}

public record AuthRule(
	string Key,
	Func<AuthenticatedUser, LambdaExpression> Expression,
	Func<ISelection, bool>? ShouldApply = null
);

public record FieldStuff
{
	public bool UseAuth { get; init; }
	public List<Func<AuthenticatedUser, bool>> PreExecutionRules { get; init; } = new();
	// public List<AuthRule> UseAuth { get; init; }
}

public record AuthenticatedUser(
	int Id
);
