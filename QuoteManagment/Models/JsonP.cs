using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;


namespace QuoteManagement.Models
{
    public class JsonpResult : JsonResult
    {

        public JsonpResult(object value): base (value)
        {  }

        /// <summary>
        /// Gets or sets the javascript callback function that is
        /// to be invoked in the resulting script output.
        /// </summary>
        /// <value>The callback function name.</value>
        public string Callback { get; set; }
        public override void ExecuteResult(ActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");

            var response = context.HttpContext.Response;
            if (!String.IsNullOrEmpty(ContentType))
                response.ContentType = ContentType;
            else
                response.ContentType = "application/javascript";

            //if (ContentEncoding != null)
            //    response.ContentEncoding = ContentEncoding;

            if (Callback == null || Callback.Length == 0)
                Callback = context.HttpContext.Request.Query["callback"].ToString();

            if (Value != null)
            {

                string ser = JsonConvert.SerializeObject(Value, SerializerSettings);
                response.Body.Write(Encoding.ASCII.GetBytes( Callback + "(" + ser + ");"));
            }
   
        }
    }
}