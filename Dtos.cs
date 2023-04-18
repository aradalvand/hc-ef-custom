using System.ComponentModel;

namespace hc_ef_custom;

// Benefits of this approach:
// - No boxing
// - No "materializing" logic
// - We can directly return the result with no modification
// - Inheritance checks and results would be easier
public abstract class BaseDto
{
	[EditorBrowsable(EditorBrowsableState.Never)]
	public IReadOnlyDictionary<string, bool> _Meta { get; init; } = default!;
}

public abstract class BaseEntityDto : BaseDto
{
	public int Id { get; init; } = default!;
}

public class CourseDto : BaseEntityDto
{
	public string Title { get; init; } = default!;
	public double? AverageRating { get; init; } = default!;
	public int LessonsCount { get; init; } = default!;
	public VideoDto PreviewVideo { get; init; } = default!;
	public InstructorDto Instructor { get; init; } = default!;
	public IEnumerable<LessonDto> Lessons { get; init; } = default!;
}

public class RatingDto : BaseEntityDto
{
	public CourseDto Course { get; init; } = default!;
	public byte Stars { get; init; } = default!;
}

public class InstructorDto : BaseEntityDto
{
	public string FirstName { get; init; } = default!;
	public string LastName { get; init; } = default!;
	public string FullName { get; init; } = default!;
	public IEnumerable<CourseDto> Courses { get; init; } = default!;
}

public abstract class LessonDto : BaseEntityDto
{
	public string Title { get; init; } = default!;

	public CourseDto Course { get; init; } = default!;
}

public class VideoLessonDto : LessonDto
{
	public Uri Url { get; init; } = default!;

	public VideoDto Video { get; init; } = default!;
}

public class ArticleLessonDto : LessonDto
{
	public string Text { get; init; } = default!;
}

public class ImageDto : BaseDto
{
	public Guid Id { get; init; } = default!;
	public string Blurhash { get; init; } = default!;
}

public class VideoDto : BaseDto
{
	public Guid Id { get; init; } = default!;
	public ImageDto Thumbnail { get; init; } = default!;
}

public class CourseType : ObjectType<CourseDto>
{
	protected override void Configure(IObjectTypeDescriptor<CourseDto> descriptor)
	{
		descriptor.Mapped().To<Course>(d =>
		{
			d.Property(c => c.Title)
				.UseAuth(a => a
					.MustBeAuthenticated()
					.MustHaveRole(UserRole.Admin)
				);

			d.Property(c => c.AverageRating)
				.Format(value => Math.Round(value!.Value, 1));

			// d.Property(c => c.AverageRating)
			// 	.UseAuth(a => a
			// 		.MustBeAuthenticated()
			// 		.Must((currentUser, course) =>
			// 			currentUser!.Role == UserRole.Admin || course.Id > currentUser!.Id
			// 		)
			// 	);

			d.Property(c => c.LessonsCount).MapTo(c => c.Lessons.Count)
				.UseAuth(a => a
					.MustBeAuthenticated()
					.Must((currentUser, course) =>
						course.Ratings.Any(r => r.Stars < currentUser!.Id)
					)
				);

			d.Property(c => c.Lessons).MapTo(c => c.Lessons.Where(l => l.Title.StartsWith("Foo")));
		});
	}
}
public class InstructorType : ObjectType<InstructorDto>
{
	protected override void Configure(IObjectTypeDescriptor<InstructorDto> descriptor)
	{
		descriptor.Mapped().To<Instructor>();
	}
}
public class RatingType : ObjectType<RatingDto>
{
	protected override void Configure(IObjectTypeDescriptor<RatingDto> descriptor)
	{
		descriptor.Mapped().To<Rating>();
	}
}
public class LessonType : InterfaceType<LessonDto>
{
	protected override void Configure(IInterfaceTypeDescriptor<LessonDto> descriptor)
	{
		descriptor.Mapped().To<Lesson>(c =>
		{
			// c.Property(c => c.Title).MapTo(c => "(" + c.Title + ")");
		});
	}
}
public class VideoLessonType : ObjectType<VideoLessonDto>
{
	protected override void Configure(IObjectTypeDescriptor<VideoLessonDto> descriptor)
	{
		descriptor.Mapped().To<VideoLesson>(c =>
		{
			c.Property(c => c.Title).MapTo(c => "((" + c.Title + "))");
			c.Property(c => c.Video)
				.UseAuth(a => a
					.MustBeAuthenticated()
					.Must((currentUser, l) => l.Course.Ratings.Count() > 1)
					.WhenSelected(v => v.Id)
				);
		});
	}
}
public class ArticleLessonType : ObjectType<ArticleLessonDto>
{
	protected override void Configure(IObjectTypeDescriptor<ArticleLessonDto> descriptor)
	{
		descriptor.Mapped().To<ArticleLesson>();
	}
}

public class ImageType : ObjectType<ImageDto>
{
	protected override void Configure(IObjectTypeDescriptor<ImageDto> descriptor)
	{
		descriptor.Mapped().To<Image>();
	}
}

public class VideoType : ObjectType<VideoDto>
{
	protected override void Configure(IObjectTypeDescriptor<VideoDto> descriptor)
	{
		descriptor.Mapped().To<Video>();
	}
}
