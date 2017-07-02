using System.Web.Mvc;

namespace StaticFileRefresh.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View("Index");
        }
    }
}