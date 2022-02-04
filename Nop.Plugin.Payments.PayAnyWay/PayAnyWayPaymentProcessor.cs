using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Plugins;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;
using System.Threading.Tasks;
using Nop.Services.Orders;

namespace Nop.Plugin.Payments.PayAnyWay
{
    /// <summary>
    /// PayAnyWay payment method
    /// </summary>
    public class PayAnyWayPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly ICurrencyService _currencyService;
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly CurrencySettings _currencySettings;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;

        const string MONETA_URL = "https://www.moneta.ru/assistant.htm";
        const string DEMO_MONETA_URL = "https://demo.moneta.ru/assistant.htm";

        #endregion

        #region Ctor

        public PayAnyWayPaymentProcessor(ICurrencyService currencyService,
            ILocalizationService localizationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            CurrencySettings currencySettings,
            IHttpContextAccessor httpContextAccessor,
            IOrderTotalCalculationService orderTotalCalculationService)
        {
            _currencyService = currencyService;
            _localizationService = localizationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _currencySettings = currencySettings;
            _httpContextAccessor = httpContextAccessor;
            _orderTotalCalculationService = orderTotalCalculationService;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending });
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var customerId = postProcessPaymentRequest.Order.CustomerId;
            var orderGuid = postProcessPaymentRequest.Order.OrderGuid;
            var orderTotal = postProcessPaymentRequest.Order.OrderTotal;

            var currencyCode = (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId)).CurrencyCode;
            var payAnyWayPaymentSettings = await _settingService.LoadSettingAsync<PayAnyWayPaymentSettings>(_storeContext.GetCurrentStore().Id);

            var model = PayAnyWayPaymentRequest.CreatePayAnyWayPaymentRequest(payAnyWayPaymentSettings, customerId, orderGuid, orderTotal, currencyCode);

            //create and send post data
            var post = new RemotePost(_httpContextAccessor,_webHelper)
            {
                FormName = "PayPoint",
                Url = payAnyWayPaymentSettings.MntDemoArea ? DEMO_MONETA_URL : MONETA_URL
            };
            post.Add("MNT_ID", model.MntId);
            post.Add("MNT_TRANSACTION_ID", model.MntTransactionId);
            post.Add("MNT_CURRENCY_CODE", model.MntCurrencyCode);
            post.Add("MNT_AMOUNT", model.MntAmount);
            post.Add("MNT_TEST_MODE", model.MntTestMode.ToString());
            post.Add("MNT_SUBSCRIBER_ID", model.MntSubscriberId.ToString());
            post.Add("MNT_SIGNATURE", model.MntSignature);
            var siteUrl = _webHelper.GetStoreLocation();
            var failUrl = $"{siteUrl}Plugins/PayAnyWay/CancelOrder";
            var successUrl = $"{siteUrl}Plugins/PayAnyWay/Success";
            var returnUrl = $"{siteUrl}Plugins/PayAnyWay/Return";
            post.Add("MNT_FAIL_URL", failUrl);
            post.Add("MNT_SUCCESS_URL", successUrl);
            post.Add("MNT_RETURN_URL", returnUrl);

            post.Post();
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>Additional handling fee</returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            var payAnyWayPaymentSettings = await _settingService.LoadSettingAsync<PayAnyWayPaymentSettings>(_storeContext.GetCurrentStore().Id);

            var result = await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
                payAnyWayPaymentSettings.AdditionalFee, payAnyWayPaymentSettings.AdditionalFeePercentage);

            return result;
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            return Task.FromResult(!((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5));
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentPayAnyWay/Configure";
        }

        public string GetPublicViewComponentName()
        {
            return "PaymentPayAnyWay";
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Install plugin method
        /// </summary>
        public override async Task InstallAsync()
        {
            //settings
            var settings = new PayAnyWayPaymentSettings
            {
                MntTestMode = true,
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntId", "Идентификатор магазина");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntId.Hint", "Укажите номер счета Вашего магазина. Получить его можно в личном кабинете на сайте http://moneta.ru. (в документации данное поле соответствует параметру MNT_ID).");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntTestMode", "Тестовый режим");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntTestMode.Hint", "Если выбрано, то все запросы к платежному сервису будут выполняться в тестовом режиме, то есть реального списания денег производится не будет. Внимание, для корректной работы данной функции она должна быть активирована одновременно в настройках плагина и счета.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntDemoArea", "Использовать демо площадку");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntDemoArea.Hint", "Если выбрано, то все запросы к платежному сервису будут выполняться на тестовой площадке, а не на основном сайте. (Подробней о демо площадке вы можете узнать в документации к MONETA.Assistant)");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.Hashcode", "Код проверки целостности данных");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.Hashcode.Hint", "Укажите код проверки целостности данных. Получить его можно в личном кабинете на сайте http://moneta.ru.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFee", "Комиссия");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFee.Hint", "Введите дополнительную плату, взымаемую с клиентов.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFeePercentage", "Комиссия в процентах");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFeePercentage.Hint", "Определяет, следует ли применять процентную комиссию от общей стоимости заказа. Если не включен, используется фиксированная комиссия.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.RedirectionTip", "Для оплаты Вы будете перенаправлены на сайт MONETA.RU.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.PaymentMethodDescription", "Для оплаты Вы будете перенаправлены на сайт MONETA.RU.");
            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall plugin method
        /// </summary>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<PayAnyWayPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntId");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntTestMode");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntDemoArea");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.Hashcode");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntId.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntTestMode.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.MntDemoArea.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.Hashcode.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFee");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFee.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFeePercentage");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.AdditionalFeePercentage.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.RedirectionTip");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.PayAnyWay.Fields.PaymentMethodDescription");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.PayAnyWay.Fields.PaymentMethodDescription");
        }

        #endregion
    }
}