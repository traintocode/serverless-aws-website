using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Scriban;
using System.Text;
using System.Web;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ServerlessAwsWebsite;

public class LambdaFunctions
{
    // AWS clients can just be instantiated with no arguments like this...
    private readonly IAmazonS3 _s3Client = new AmazonS3Client();

    // Read the environment variables into this object...
    private readonly (
        string HtmlBucketName,
        string SongBucketName,
        string PublicWebsiteUrl) envVars =
    (
        HtmlBucketName: Environment.GetEnvironmentVariable("HTML_BUCKET_NAME")!,
        SongBucketName: Environment.GetEnvironmentVariable("SONG_BUCKET_NAME")!,
        PublicWebsiteUrl: Environment.GetEnvironmentVariable("PUBLIC_WEBSITE_URL")!
    );

    /// <summary>
    /// The entry point to the AWS Lambda function (set in serverless.template)
    /// </summary>
    public async Task<string> RenderHtml(S3Event @event, ILambdaContext context)
    {
        var s3Event = @event.Records?[0].S3 ?? throw new ArgumentException("No S3 event!");
        var objectKey = s3Event.Object.Key.Replace("+", " ");
        var songTitle = objectKey.Replace(".txt", "", StringComparison.InvariantCultureIgnoreCase);

        // The event that triggered this lambda only contains the key, so go off and get the entire object...
        var s3GetResponse = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = s3Event.Bucket.Name,
            Key = objectKey
        });

        // ...and read it as a string (these will be text files)
        using var reader = new StreamReader(s3GetResponse.ResponseStream, Encoding.UTF8);
        var objectContentString = reader.ReadToEnd();

        // See https://github.com/scriban/scriban for notes on template syntax in song.html
        var template = Template.Parse(await File.ReadAllTextAsync("song.html"));

        // Render our (static) html page for this song
        var songPageHtml = template.Render(new { Lyrics = objectContentString, Title = songTitle });

        // Save the rendered html page to the publicly facing html bucket
        var htmlObjectKey = "Song/" + songTitle;
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = envVars.HtmlBucketName,
            Key = htmlObjectKey,
            ContentBody = songPageHtml,
            ContentType = "text/html"
        });

        context.Logger.LogInformation($"Rendered and saved html page for S3 key {objectKey}");
        context.Logger.LogInformation($"This page will be available at:");
        context.Logger.LogInformation(Path.Join(envVars.PublicWebsiteUrl, HttpUtility.UrlPathEncode(htmlObjectKey)));
        return "ok";

    }
};

