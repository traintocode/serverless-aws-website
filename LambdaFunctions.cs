using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Scriban;
using System.Text;
using System.Web;
using System.Text.Json;
using Amazon.Comprehend;
using Amazon.Comprehend.Model;

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
    /// Reads the body of the lyrics file from this S3 key and saves to the state object
    /// </summary>
    public async Task<StepFunctionsState> ReadLyricsFromS3(StepFunctionsState state, ILambdaContext context)
    {
        context.Logger.LogInformation("Executing function...");
        context.Logger.LogInformation(JsonSerializer.Serialize(state));

        state.TriggeredS3Key = state.Detail?.Object?.Key ?? throw new ArgumentException("Missing S3 object key in state");


        var objectKey = state.TriggeredS3Key.Replace("+", " ");
        var songTitle = objectKey.Replace(".txt", "", StringComparison.InvariantCultureIgnoreCase);

        // The event that triggered this lambda only contains the key, so go off and get the entire object...
        var s3GetResponse = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = envVars.SongBucketName,
            Key = objectKey
        });

        // ...and read it as a string (these will be text files)
        using var reader = new StreamReader(s3GetResponse.ResponseStream, Encoding.UTF8);
        var objectContentString = reader.ReadToEnd();

        // Save the title and contents to the state object
        state.SongTitle = songTitle;
        state.SongLyrics = objectContentString;

        return state;
    }

    /// <summary>
    /// Uses AWS Comprehend to detect the language this song was written in
    /// </summary>
    public async Task<StepFunctionsState> DetectSongLanguage(StepFunctionsState state, ILambdaContext context)
    {
        var comprehendClient = new AmazonComprehendClient();

        var detectDominantLanguageResponse = await comprehendClient.DetectDominantLanguageAsync(new DetectDominantLanguageRequest
        {
            Text = state.SongLyrics
        });
        foreach (var dl in detectDominantLanguageResponse.Languages)
        {
            context.Logger.LogInformation($"Language Code: {dl.LanguageCode}, Score: {dl.Score}");
        }
        var dominantLanguageKey = detectDominantLanguageResponse.Languages.Select(x => x.LanguageCode).First();

        state.LanguageKey = dominantLanguageKey;

        return state;
    }

    /// <summary>
    /// Renders using the English language html template
    /// </summary>
    public async Task<StepFunctionsState> RenderInEnglish(StepFunctionsState state, ILambdaContext context)
    {
        await RenderHtml("song.en.html", new SongHtmlViewModel(state.SongLyrics!, state.SongTitle!), context.Logger.LogInformation);

        return state;

    }

    /// <summary>
    /// Renders using the French language html template
    /// </summary>
    public async Task<StepFunctionsState> RenderInFrench(StepFunctionsState state, ILambdaContext context)
    {

        await RenderHtml("song.fr.html", new SongHtmlViewModel(state.SongLyrics!, state.SongTitle!), context.Logger.LogInformation);

        return state;

    }

    public record SongHtmlViewModel(string Lyrics, string Title);

    public async Task RenderHtml(string templateFile, SongHtmlViewModel model, Action<string> log)
    {
        var template = Template.Parse(await File.ReadAllTextAsync(templateFile));

        // Render our (static) html page for this song
        var songPageHtml = template.Render(model);

        // Save the rendered html page to the publicly facing html bucket
        var htmlObjectKey = "Song/" + model.Title;
        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = envVars.HtmlBucketName,
            Key = htmlObjectKey,
            ContentBody = songPageHtml,
            ContentType = "text/html"
        });

        
        log($"Rendered and saved html page for song {model.Title}");
        log($"This page will be available at:");
        log(Path.Join(envVars.PublicWebsiteUrl, HttpUtility.UrlPathEncode(htmlObjectKey)));
    }
};

