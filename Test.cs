using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Execution.Processing;

namespace hc_ef_custom;

public static class WellKnownContextKeys
{
	public const string CorrespondingEntityType = nameof(CorrespondingEntityType);
	// TODO: Put all the field-related stuff in a single context value as an object?
	public const string PropertyProjectionExpression = nameof(PropertyProjectionExpression);
	public const string UseAuth = nameof(UseAuth);
	public const string Meta = nameof(Meta);
}

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

	public void To<TEntity>(Action<MappingDescriptor<TDto, TEntity>> configure)
	{
		configure(new(_descriptor));

		_descriptor.Ignore(d => d._Meta);
		// _descriptor.Extend().Definition.ContextData[]
		_descriptor.Extend().OnBeforeCreate(d =>
		{
			d.ContextData[WellKnownContextKeys.CorrespondingEntityType] = typeof(TEntity);
		});
		foreach (PropertyInfo prop in typeof(TDto).GetProperties(BindingFlags.Public))
		{
			// TODO: Make sure all the other properties have mappings/register mappings for the non-explicitly-mapped properties.
		}
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
		// TODO: Is this needed or does the call to `Field` below take care of it?
		// if (propertySelector is not MemberExpression memberExpr
		// 	|| memberExpr.Member.DeclaringType != typeof(TDto))
		// 	throw new InvalidOperationException();

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
		_descriptor.Extend().OnBeforeCreate(d => // todo
		{
			d.ContextData[WellKnownContextKeys.PropertyProjectionExpression] = map;
		});
		return this;
	}

	public PropertyMappingDescriptor<TDto, TEntity, TProperty> UseAuth(
		Action<PropertyAuthMappingDescriptor<TDto, TEntity, TProperty>> configure
	)
	{
		_descriptor.Extend().OnBeforeCreate(d => // todo
		{
			d.ContextData[WellKnownContextKeys.UseAuth] = true;
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
		Expression<Func<TEntity, bool>> ruleExpression
	)
	{
		string key = Guid.NewGuid().ToString("N"); // Does this work?
		AuthRule rule = new(key, ruleExpression);
		_descriptor.Extend().OnBeforeCreate(d =>
		{
			if (d.ContextData.GetValueOrDefault(WellKnownContextKeys.Meta) is List<AuthRule> authRules)
				authRules.Add(rule);
			else
				d.ContextData[WellKnownContextKeys.Meta] = new List<AuthRule> { rule };
		});
		_descriptor.Use(next => async context =>
		{
			await next(context);
			var result = context.Parent<CourseDto>()._Meta[key];
			if (result)
				Console.WriteLine("Permitted.");
			else
				Console.WriteLine("Not permitted.");
		});

		return this;
	}
}

public record AuthRule(
	string Key,
	LambdaExpression Expression,
	Func<ISelection, bool>? ShouldApply = null
);


// public class MappedObjectType<TDto, TEntity> : ObjectType<TEntity>
// {

// }
