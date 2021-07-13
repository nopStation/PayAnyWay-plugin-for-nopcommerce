using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Payments.PayAnyWay.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.PayAnyWay.Controllers
{
    public class PaymentPayAnyWayController : BasePaymentController
    {
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentService _paymentService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly INotificationService _notificationService;

        public PaymentPayAnyWayController(ILocalizationService localizationService,
            ILogger logger,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentService paymentService,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            INotificationService notificationService)
        {
            _localizationService = localizationService;
            _logger = logger;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentService = paymentService;
            _permissionService = permissionService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _notificationService = notificationService;
        }

        private async Task<bool> CheckOrderDataAsync(Order order, string operationId, string signature, string currencyCode)
        {
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var payAnyWayPaymentSettings = await _settingService.LoadSettingAsync<PayAnyWayPaymentSettings>(storeScope);

            var model = PayAnyWayPaymentRequest.CreatePayAnyWayPaymentRequest(payAnyWayPaymentSettings, order.CustomerId, order.OrderGuid, order.OrderTotal, currencyCode);

            var checkDataString = $"{model.MntId}{model.MntTransactionId}{operationId}{model.MntAmount}{model.MntCurrencyCode.ToUpper()}{model.MntSubscriberId}{model.MntTestMode}{model.MntHashcode}";

            return model.GetMD5(checkDataString) == signature;
        }

        private async Task<ContentResult> GetResponseAsync(string textToResponse, bool success = false)
        {
            var msg = success ? "SUCCESS" : "FAIL";

            if (!success)
                await _logger.ErrorAsync($"PayAnyWay. {textToResponse}");

            return Content($"{msg}\r\nnopCommerce. {textToResponse}", "text/plain", Encoding.UTF8);
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var payAnyWayPaymentSettings = await _settingService.LoadSettingAsync<PayAnyWayPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MntId = payAnyWayPaymentSettings.MntId,
                MntTestMode = payAnyWayPaymentSettings.MntTestMode,
                MntDemoArea = payAnyWayPaymentSettings.MntDemoArea,
                Hashcode = payAnyWayPaymentSettings.Hashcode,
                AdditionalFee = payAnyWayPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = payAnyWayPaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope > 0)
            {
                model.MntIdOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.MntId, storeScope);
                model.MntTestModeOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.MntTestMode, storeScope);
                model.MntDemoAreaOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.MntDemoArea, storeScope);
                model.HashcodeOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.Hashcode, storeScope);
                model.AdditionalFeeOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.AdditionalFee, storeScope);
                model.AdditionalFeePercentageOverrideForStore = await _settingService.SettingExistsAsync(payAnyWayPaymentSettings, x => x.AdditionalFeePercentage, storeScope);
            }

            return View("~/Plugins/Payments.PayAnyWay/Views/Configure.cshtml", model);
        }
        
        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var payAnyWayPaymentSettings = await _settingService.LoadSettingAsync<PayAnyWayPaymentSettings>(storeScope);

            //save settings
            payAnyWayPaymentSettings.MntId = model.MntId;
            payAnyWayPaymentSettings.MntTestMode = model.MntTestMode;
            payAnyWayPaymentSettings.MntDemoArea = model.MntDemoArea;
            payAnyWayPaymentSettings.Hashcode = model.Hashcode;
            payAnyWayPaymentSettings.AdditionalFee = model.AdditionalFee;
            payAnyWayPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.MntId, model.MntIdOverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.MntTestMode, model.MntTestModeOverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.MntDemoArea, model.MntDemoAreaOverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.Hashcode, model.HashcodeOverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.AdditionalFee, model.AdditionalFeeOverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(payAnyWayPaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentageOverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();

            _notificationService. SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return RedirectToAction("Configure");
        }


        public async Task<IActionResult> ConfirmPay()
        {
            var orderId = _webHelper.QueryString<string>("MNT_TRANSACTION_ID");
            var signature = _webHelper.QueryString<string>("MNT_SIGNATURE");
            var operationId = _webHelper.QueryString<string>("MNT_OPERATION_ID");
            var currencyCode = _webHelper.QueryString<string>("MNT_CURRENCY_CODE").ToUpper();

            if (!Guid.TryParse(orderId, out Guid orderGuid))
            {
                return await GetResponseAsync("Invalid order GUID");
            }

            var order = await _orderService.GetOrderByGuidAsync(orderGuid);
            if (order == null)
            {
                return await GetResponseAsync("Order cannot be loaded");
            }

            var sb = new StringBuilder();
            sb.AppendLine("PayAnyWay:");
            try
            {
                foreach (var kvp in Request.Query)
                {
                    sb.AppendLine(kvp.Key + ": " + kvp.Value);
                }
            }
            catch (InvalidCastException)
            {
                await _logger.WarningAsync("PayAnyWay. Can't cast HttpContext.Request.QueryString");
            }

            //order note
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                Note = sb.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            //check order data by signature
            if (!await CheckOrderDataAsync(order, operationId, signature, currencyCode))
            {
                return await GetResponseAsync("Invalid order data");
            }

            //mark order as paid
            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                await _orderProcessingService.MarkOrderAsPaidAsync(order);
            }

            return await GetResponseAsync("Your order has been paid", true);
        }

        public async Task<IActionResult> Success()
        {
            var orderId = _webHelper.QueryString<string>("MNT_TRANSACTION_ID");
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
            {
                order = await _orderService.GetOrderByGuidAsync(orderGuid);
            }

            if (order == null)
                return RedirectToAction("Index", "Home", new {area = string.Empty});

            return RedirectToRoute("CheckoutCompleted", new {orderId = order.Id});
        }

        public async Task<IActionResult> CancelOrder()
        {
            var orderId = _webHelper.QueryString<string>("MNT_TRANSACTION_ID");
            Order order = null;

            if (Guid.TryParse(orderId, out Guid orderGuid))
            {
                order = await _orderService.GetOrderByGuidAsync(orderGuid);
            }

            if (order == null)
                return RedirectToAction("Index", "Home", new {area = string.Empty});

            return RedirectToRoute("OrderDetails", new {orderId = order.Id});
        }

        public IActionResult Return()
        {
           return RedirectToAction("Index", "Home", new { area = string.Empty });
        }
    }
}