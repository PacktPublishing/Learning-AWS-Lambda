using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializerAttribute(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace AWSServerless1
{
    public class Functions
    {
        // This const is the name of the environment variable that the serverless.template will use to set
        // the name of the DynamoDB table used to store blog posts.
        const string TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP = "BlogTable";

        public const string ID_QUERY_STRING_NAME = "Id";
        IDynamoDBContext DDBContext { get; set; }

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
            // Check to see if a table name was passed in through environment variables and if so 
            // add the table mapping.
            var tableName = System.Environment.GetEnvironmentVariable(TABLENAME_ENVIRONMENT_VARIABLE_LOOKUP);
            if(!string.IsNullOrEmpty(tableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(Blog)] = new Amazon.Util.TypeMapping(typeof(Blog), tableName);
            }

            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.DDBContext = new DynamoDBContext(new AmazonDynamoDBClient(), config);
        }

        /// <summary>
        /// Constructor used for testing passing in a preconfigured DynamoDB client.
        /// </summary>
        /// <param name="ddbClient"></param>
        /// <param name="tableName"></param>
        public Functions(IAmazonDynamoDB ddbClient, string tableName)
        {
            if (!string.IsNullOrEmpty(tableName))
            {
                AWSConfigsDynamoDB.Context.TypeMappings[typeof(Blog)] = new Amazon.Util.TypeMapping(typeof(Blog), tableName);
            }

            var config = new DynamoDBContextConfig { Conversion = DynamoDBEntryConversion.V2 };
            this.DDBContext = new DynamoDBContext(ddbClient, config);
        }

        /// <summary>
        /// A Lambda function that returns back a page worth of blog posts.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>The list of blogs</returns>
        public async Task<APIGatewayProxyResponse> GetBlogsAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            context.Logger.LogLine("Getting blogs");
            var search = this.DDBContext.ScanAsync<Blog>(null);
            var page = await search.GetNextSetAsync();
            context.Logger.LogLine($"Found {page.Count} blogs");

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(page),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };

            return response;
        }

        /// <summary>
        /// A Lambda function that returns the blog identified by blogId
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> GetBlogAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string blogId = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(ID_QUERY_STRING_NAME))
                blogId = request.PathParameters[ID_QUERY_STRING_NAME];
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(ID_QUERY_STRING_NAME))
                blogId = request.QueryStringParameters[ID_QUERY_STRING_NAME];

            if (string.IsNullOrEmpty(blogId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {ID_QUERY_STRING_NAME}"
                };
            }

            context.Logger.LogLine($"Getting blog {blogId}");
            var blog = await DDBContext.LoadAsync<Blog>(blogId);
            context.Logger.LogLine($"Found blog: {blog != null}");

            if (blog == null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.NotFound
                };
            }

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = JsonConvert.SerializeObject(blog),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
            return response;
        }

        /// <summary>
        /// A Lambda function that adds a blog post.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task<APIGatewayProxyResponse> AddBlogAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            var blog = JsonConvert.DeserializeObject<Blog>(request?.Body);
            blog.Id = Guid.NewGuid().ToString();
            blog.CreatedTimestamp = DateTime.Now;

            context.Logger.LogLine($"Saving blog with id {blog.Id}");
            await DDBContext.SaveAsync<Blog>(blog);

            var response = new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK,
                Body = blog.Id.ToString(),
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
            return response;
        }

        /// <summary>
        /// A Lambda function that removes a blog post from the DynamoDB table.
        /// </summary>
        /// <param name="request"></param>
        public async Task<APIGatewayProxyResponse> RemoveBlogAsync(APIGatewayProxyRequest request, ILambdaContext context)
        {
            string blogId = null;
            if (request.PathParameters != null && request.PathParameters.ContainsKey(ID_QUERY_STRING_NAME))
                blogId = request.PathParameters[ID_QUERY_STRING_NAME];
            else if (request.QueryStringParameters != null && request.QueryStringParameters.ContainsKey(ID_QUERY_STRING_NAME))
                blogId = request.QueryStringParameters[ID_QUERY_STRING_NAME];

            if (string.IsNullOrEmpty(blogId))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                    Body = $"Missing required parameter {ID_QUERY_STRING_NAME}"
                };
            }

            context.Logger.LogLine($"Deleting blog with id {blogId}");
            await this.DDBContext.DeleteAsync<Blog>(blogId);

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.OK
            };
        }
    }
}
