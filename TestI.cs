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

	public void To<TEntity>(Action<MappingDescriptorI<TDto, TEntity>>? configure = null)
	{
		configure?.Invoke(new(_descriptor));

		_descriptor.Name(typeof(TEntity).Name);

		_descriptor.Ignore(d => d._Meta); // NOTE: We do our configuration (such as ignoring the meta property) after the user code, because we want it to take precedence.

		_descriptor.Extend().OnBeforeCreate((c, d) =>
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"Type OnBeforeCreate: {d.Name}");
			Console.ResetColor();
			Console.WriteLine("---");
			Mappings.Types[typeof(TEntity)] = typeof(TDto);
			Mappings.Types[typeof(TDto)] = typeof(TEntity);
		});

		_descriptor.Extend().OnBeforeCompletion((c, d) =>
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine($"Type OnBeforeCompletion: {d.Name}");
			Console.ResetColor();
			Console.WriteLine("---");
			foreach (var field in d.Fields) // NOTE: We examine the type's fields right before the configuration is all done so that we operate upon exactly the fields that are going to be part of the type in the schema. The user might have removed (ignored) or added fields before this.
			{
				if (field.Member is null)
					throw new InvalidOperationException("All fields in a mapped type must correspond to a property on the DTO type.");  // NOTE: This prevents the user from creating arbitrary new fields (e.g. `descriptor.Field("FooBar")`).

				var dtoProp = (PropertyInfo)field.Member; // NOTE: We assume the member behind the field is a property (and this assumption in practically safe in our case, although not safe in principle, if you will)
				if (Mappings.PropertyExpressions.ContainsKey(dtoProp))
					continue;

				var namesakeEntityProp = typeof(TEntity).GetProperty(dtoProp.Name); // NOTE: Property on the entity type with the same name.
				if (
					namesakeEntityProp is null ||
					!Helpers.AreAssignable(dtoProp.PropertyType, namesakeEntityProp.PropertyType)
				)
					throw new InvalidOperationException($"Property '{dtoProp.Name}' on the DTO type '{typeof(TDto)}' was not configured explicitly and no implicitly matching property with the same name and type on the entity type was found..");

				// NOTE: Doing this here as opposed to in the projection middleware has two advantages: 1. No reflection at runtime (only on startup) 2. If no matching entity property exists we throw on startup instead of at runtime.
				var param = Expression.Parameter(typeof(TEntity));
				var body = Expression.Property(param, namesakeEntityProp);
				var expression = Expression.Lambda(body, param);
				Mappings.PropertyExpressions[dtoProp] = expression;
			}
		});
	}
}

public class MappingDescriptorI<TDto, TEntity>
{
	private readonly IInterfaceTypeDescriptor<TDto> _descriptor;

	public MappingDescriptorI(IInterfaceTypeDescriptor<TDto> descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyMappingDescriptorI<TDto, TEntity> Property(
		Expression<Func<TDto, object>> propertySelector
	) => new(_descriptor.Field(propertySelector));
}

public class PropertyMappingDescriptorI<TDto, TEntity>
{
	private readonly IInterfaceFieldDescriptor _descriptor;

	public PropertyMappingDescriptorI(IInterfaceFieldDescriptor descriptor)
	{
		_descriptor = descriptor;
	}

	public PropertyMappingDescriptorI<TDto, TEntity> MapTo<TResult>(
		Expression<Func<TEntity, TResult?>> map
	)
	{
		_descriptor.Extend().OnBeforeCreate(d =>
		{
			var property = (PropertyInfo)d.Member!;
			if (!Helpers.AreAssignable(property.PropertyType, typeof(TResult)))
				throw new InvalidOperationException();
			Mappings.PropertyExpressions[(PropertyInfo)d.Member!] = map;
		});
		return this;
	}
}
