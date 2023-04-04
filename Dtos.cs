using System.ComponentModel;

// Benefits of this approach:
// - No boxing
// - No "materializing" logic
// - We can directly return the result with no modification
// - Inheritance checks and results would be easier
public abstract class BaseDto
{
	public int Id { get; init; } = default!;

	[GraphQLIgnore]
	[EditorBrowsable(EditorBrowsableState.Never)]
	public IDictionary<string, bool> _Meta { get; init; } = default!;
}
public class CourseDto : BaseDto
{
	public string Title { get; init; } = default!;
	public double AverageRating { get; init; } = default!;
	public InstructorDto Instructor { get; init; } = default!;
	public IEnumerable<LessonDto> Lessons { get; init; } = default!;
}

public class RatingDto : BaseDto
{
	public CourseDto Course { get; set; } = default!;
	public byte Stars { get; init; } = default!;
}

public class InstructorDto : BaseDto
{
	public string FirstName { get; init; } = default!;
	public string LastName { get; init; } = default!;
	public string FullName { get; init; } = default!;
	public IEnumerable<CourseDto> Courses { get; init; } = default!;
}

public abstract class LessonDto : BaseDto
{
	public string Title { get; set; } = default!;

	public CourseDto Course { get; set; } = default!;
}

public class VideoLessonDto : LessonDto
{
	public required Uri Url { get; set; }
}

public class ArticleLessonDto : LessonDto
{
	public required string Text { get; set; }
}
