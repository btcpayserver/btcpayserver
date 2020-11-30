(function () {
  'use strict';

  //Copyright 2014-2015 Google Inc. All rights reserved.

  //Use of this source code is governed by a BSD-style
  //license that can be found in the LICENSE file or at
  //https://developers.google.com/open-source/licenses/bsd

  /**
   * @fileoverview The U2F api.
   */
  // 'use strict';


  /**
   * Namespace for the U2F api.
   * @type {Object}
   */
  var u2f = u2f || {};

  const chromeApi = u2f; // Adaptation for u2f-api package

  /**
   * FIDO U2F Javascript API Version
   * @number
   */
  var js_api_version;

  /**
   * The U2F extension id
   * @const {string}
   */
  // The Chrome packaged app extension ID.
  // Uncomment this if you want to deploy a server instance that uses
  // the package Chrome app and does not require installing the U2F Chrome extension.
  u2f.EXTENSION_ID = 'kmendfapggjehodndflmmgagdbamhnfd';
  // The U2F Chrome extension ID.
  // Uncomment this if you want to deploy a server instance that uses
  // the U2F Chrome extension to authenticate.
  // u2f.EXTENSION_ID = 'pfboblefjcgdjicmnffhdgionmgcdmne';


  /**
   * Message types for messsages to/from the extension
   * @const
   * @enum {string}
   */
  u2f.MessageTypes = {
    'U2F_REGISTER_REQUEST': 'u2f_register_request',
    'U2F_REGISTER_RESPONSE': 'u2f_register_response',
    'U2F_SIGN_REQUEST': 'u2f_sign_request',
    'U2F_SIGN_RESPONSE': 'u2f_sign_response',
    'U2F_GET_API_VERSION_REQUEST': 'u2f_get_api_version_request',
    'U2F_GET_API_VERSION_RESPONSE': 'u2f_get_api_version_response'
  };


  /**
   * Response status codes
   * @const
   * @enum {number}
   */
  u2f.ErrorCodes = {
    'OK': 0,
    'OTHER_ERROR': 1,
    'BAD_REQUEST': 2,
    'CONFIGURATION_UNSUPPORTED': 3,
    'DEVICE_INELIGIBLE': 4,
    'TIMEOUT': 5
  };


  //Low level MessagePort API support

  /**
   * Sets up a MessagePort to the U2F extension using the
   * available mechanisms.
   * @param {function((MessagePort|u2f.WrappedChromeRuntimePort_))} callback
   */
  u2f.getMessagePort = function(callback) {
    if (typeof chrome != 'undefined' && chrome.runtime) {
      // The actual message here does not matter, but we need to get a reply
      // for the callback to run. Thus, send an empty signature request
      // in order to get a failure response.
      var msg = {
        type: u2f.MessageTypes.U2F_SIGN_REQUEST,
        signRequests: []
      };
      chrome.runtime.sendMessage(u2f.EXTENSION_ID, msg, function() {
        if (!chrome.runtime.lastError) {
          // We are on a whitelisted origin and can talk directly
          // with the extension.
          u2f.getChromeRuntimePort_(callback);
        } else {
          // chrome.runtime was available, but we couldn't message
          // the extension directly, use iframe
          u2f.getIframePort_(callback);
        }
      });
    } else if (u2f.isAndroidChrome_()) {
      u2f.getAuthenticatorPort_(callback);
    } else if (u2f.isIosChrome_()) {
      u2f.getIosPort_(callback);
    } else {
      // chrome.runtime was not available at all, which is normal
      // when this origin doesn't have access to any extensions.
      u2f.getIframePort_(callback);
    }
  };

  /**
   * Detect chrome running on android based on the browser's useragent.
   * @private
   */
  u2f.isAndroidChrome_ = function() {
    var userAgent = navigator.userAgent;
    return userAgent.indexOf('Chrome') != -1 &&
        userAgent.indexOf('Android') != -1;
  };

  /**
   * Detect chrome running on iOS based on the browser's platform.
   * @private
   */
  u2f.isIosChrome_ = function() {
    return ["iPhone", "iPad", "iPod"].indexOf(navigator.platform) > -1;
  };

  /**
   * Connects directly to the extension via chrome.runtime.connect.
   * @param {function(u2f.WrappedChromeRuntimePort_)} callback
   * @private
   */
  u2f.getChromeRuntimePort_ = function(callback) {
    var port = chrome.runtime.connect(u2f.EXTENSION_ID,
        {'includeTlsChannelId': true});
    setTimeout(function() {
      callback(new u2f.WrappedChromeRuntimePort_(port));
    }, 0);
  };

  /**
   * Return a 'port' abstraction to the Authenticator app.
   * @param {function(u2f.WrappedAuthenticatorPort_)} callback
   * @private
   */
  u2f.getAuthenticatorPort_ = function(callback) {
    setTimeout(function() {
      callback(new u2f.WrappedAuthenticatorPort_());
    }, 0);
  };

  /**
   * Return a 'port' abstraction to the iOS client app.
   * @param {function(u2f.WrappedIosPort_)} callback
   * @private
   */
  u2f.getIosPort_ = function(callback) {
    setTimeout(function() {
      callback(new u2f.WrappedIosPort_());
    }, 0);
  };

  /**
   * A wrapper for chrome.runtime.Port that is compatible with MessagePort.
   * @param {Port} port
   * @constructor
   * @private
   */
  u2f.WrappedChromeRuntimePort_ = function(port) {
    this.port_ = port;
  };

  /**
   * Format and return a sign request compliant with the JS API version supported by the extension.
   * @param {Array<u2f.SignRequest>} signRequests
   * @param {number} timeoutSeconds
   * @param {number} reqId
   * @return {Object}
   */
  u2f.formatSignRequest_ =
      function(appId, challenge, registeredKeys, timeoutSeconds, reqId) {
        if (js_api_version === undefined || js_api_version < 1.1) {
          // Adapt request to the 1.0 JS API
          var signRequests = [];
          for (var i = 0; i < registeredKeys.length; i++) {
            signRequests[i] = {
              version: registeredKeys[i].version,
              challenge: challenge,
              keyHandle: registeredKeys[i].keyHandle,
              appId: appId
            };
          }
          return {
            type: u2f.MessageTypes.U2F_SIGN_REQUEST,
            signRequests: signRequests,
            timeoutSeconds: timeoutSeconds,
            requestId: reqId
          };
        }
        // JS 1.1 API
        return {
          type: u2f.MessageTypes.U2F_SIGN_REQUEST,
          appId: appId,
          challenge: challenge,
          registeredKeys: registeredKeys,
          timeoutSeconds: timeoutSeconds,
          requestId: reqId
        };
      };

  /**
   * Format and return a register request compliant with the JS API version supported by the extension..
   * @param {Array<u2f.SignRequest>} signRequests
   * @param {Array<u2f.RegisterRequest>} signRequests
   * @param {number} timeoutSeconds
   * @param {number} reqId
   * @return {Object}
   */
  u2f.formatRegisterRequest_ =
      function(appId, registeredKeys, registerRequests, timeoutSeconds, reqId) {
        if (js_api_version === undefined || js_api_version < 1.1) {
          // Adapt request to the 1.0 JS API
          for (var i = 0; i < registerRequests.length; i++) {
            registerRequests[i].appId = appId;
          }
          var signRequests = [];
          for (var i = 0; i < registeredKeys.length; i++) {
            signRequests[i] = {
              version: registeredKeys[i].version,
              challenge: registerRequests[0],
              keyHandle: registeredKeys[i].keyHandle,
              appId: appId
            };
          }
          return {
            type: u2f.MessageTypes.U2F_REGISTER_REQUEST,
            signRequests: signRequests,
            registerRequests: registerRequests,
            timeoutSeconds: timeoutSeconds,
            requestId: reqId
          };
        }
        // JS 1.1 API
        return {
          type: u2f.MessageTypes.U2F_REGISTER_REQUEST,
          appId: appId,
          registerRequests: registerRequests,
          registeredKeys: registeredKeys,
          timeoutSeconds: timeoutSeconds,
          requestId: reqId
        };
      };


  /**
   * Posts a message on the underlying channel.
   * @param {Object} message
   */
  u2f.WrappedChromeRuntimePort_.prototype.postMessage = function(message) {
    this.port_.postMessage(message);
  };


  /**
   * Emulates the HTML 5 addEventListener interface. Works only for the
   * onmessage event, which is hooked up to the chrome.runtime.Port.onMessage.
   * @param {string} eventName
   * @param {function({data: Object})} handler
   */
  u2f.WrappedChromeRuntimePort_.prototype.addEventListener =
      function(eventName, handler) {
        var name = eventName.toLowerCase();
        if (name == 'message' || name == 'onmessage') {
          this.port_.onMessage.addListener(function(message) {
            // Emulate a minimal MessageEvent object
            handler({'data': message});
          });
        } else {
          console.error('WrappedChromeRuntimePort only supports onMessage');
        }
      };

  /**
   * Wrap the Authenticator app with a MessagePort interface.
   * @constructor
   * @private
   */
  u2f.WrappedAuthenticatorPort_ = function() {
    this.requestId_ = -1;
    this.requestObject_ = null;
  };

  /**
   * Launch the Authenticator intent.
   * @param {Object} message
   */
  u2f.WrappedAuthenticatorPort_.prototype.postMessage = function(message) {
    var intentUrl =
        u2f.WrappedAuthenticatorPort_.INTENT_URL_BASE_ +
        ';S.request=' + encodeURIComponent(JSON.stringify(message)) +
        ';end';
    document.location = intentUrl;
  };

  /**
   * Tells what type of port this is.
   * @return {String} port type
   */
  u2f.WrappedAuthenticatorPort_.prototype.getPortType = function() {
    return "WrappedAuthenticatorPort_";
  };


  /**
   * Emulates the HTML 5 addEventListener interface.
   * @param {string} eventName
   * @param {function({data: Object})} handler
   */
  u2f.WrappedAuthenticatorPort_.prototype.addEventListener = function(eventName, handler) {
    var name = eventName.toLowerCase();
    if (name == 'message') {
      var self = this;
      /* Register a callback to that executes when
       * chrome injects the response. */
      window.addEventListener(
          'message', self.onRequestUpdate_.bind(self, handler), false);
    } else {
      console.error('WrappedAuthenticatorPort only supports message');
    }
  };

  /**
   * Callback invoked  when a response is received from the Authenticator.
   * @param function({data: Object}) callback
   * @param {Object} message message Object
   */
  u2f.WrappedAuthenticatorPort_.prototype.onRequestUpdate_ =
      function(callback, message) {
        var messageObject = JSON.parse(message.data);
        var intentUrl = messageObject['intentURL'];

        var errorCode = messageObject['errorCode'];
        var responseObject = null;
        if (messageObject.hasOwnProperty('data')) {
          responseObject = /** @type {Object} */ (
              JSON.parse(messageObject['data']));
        }

        callback({'data': responseObject});
      };

  /**
   * Base URL for intents to Authenticator.
   * @const
   * @private
   */
  u2f.WrappedAuthenticatorPort_.INTENT_URL_BASE_ =
      'intent:#Intent;action=com.google.android.apps.authenticator.AUTHENTICATE';

  /**
   * Wrap the iOS client app with a MessagePort interface.
   * @constructor
   * @private
   */
  u2f.WrappedIosPort_ = function() {};

  /**
   * Launch the iOS client app request
   * @param {Object} message
   */
  u2f.WrappedIosPort_.prototype.postMessage = function(message) {
    var str = JSON.stringify(message);
    var url = "u2f://auth?" + encodeURI(str);
    location.replace(url);
  };

  /**
   * Tells what type of port this is.
   * @return {String} port type
   */
  u2f.WrappedIosPort_.prototype.getPortType = function() {
    return "WrappedIosPort_";
  };

  /**
   * Emulates the HTML 5 addEventListener interface.
   * @param {string} eventName
   * @param {function({data: Object})} handler
   */
  u2f.WrappedIosPort_.prototype.addEventListener = function(eventName, handler) {
    var name = eventName.toLowerCase();
    if (name !== 'message') {
      console.error('WrappedIosPort only supports message');
    }
  };

  /**
   * Sets up an embedded trampoline iframe, sourced from the extension.
   * @param {function(MessagePort)} callback
   * @private
   */
  u2f.getIframePort_ = function(callback) {
    // Create the iframe
    var iframeOrigin = 'chrome-extension://' + u2f.EXTENSION_ID;
    var iframe = document.createElement('iframe');
    iframe.src = iframeOrigin + '/u2f-comms.html';
    iframe.setAttribute('style', 'display:none');
    document.body.appendChild(iframe);

    var channel = new MessageChannel();
    var ready = function(message) {
      if (message.data == 'ready') {
        channel.port1.removeEventListener('message', ready);
        callback(channel.port1);
      } else {
        console.error('First event on iframe port was not "ready"');
      }
    };
    channel.port1.addEventListener('message', ready);
    channel.port1.start();

    iframe.addEventListener('load', function() {
      // Deliver the port to the iframe and initialize
      iframe.contentWindow.postMessage('init', iframeOrigin, [channel.port2]);
    });
  };


  //High-level JS API

  /**
   * Default extension response timeout in seconds.
   * @const
   */
  u2f.EXTENSION_TIMEOUT_SEC = 30;

  /**
   * A singleton instance for a MessagePort to the extension.
   * @type {MessagePort|u2f.WrappedChromeRuntimePort_}
   * @private
   */
  u2f.port_ = null;

  /**
   * Callbacks waiting for a port
   * @type {Array<function((MessagePort|u2f.WrappedChromeRuntimePort_))>}
   * @private
   */
  u2f.waitingForPort_ = [];

  /**
   * A counter for requestIds.
   * @type {number}
   * @private
   */
  u2f.reqCounter_ = 0;

  /**
   * A map from requestIds to client callbacks
   * @type {Object.<number,(function((u2f.Error|u2f.RegisterResponse))
   *                       |function((u2f.Error|u2f.SignResponse)))>}
   * @private
   */
  u2f.callbackMap_ = {};

  /**
   * Creates or retrieves the MessagePort singleton to use.
   * @param {function((MessagePort|u2f.WrappedChromeRuntimePort_))} callback
   * @private
   */
  u2f.getPortSingleton_ = function(callback) {
    if (u2f.port_) {
      callback(u2f.port_);
    } else {
      if (u2f.waitingForPort_.length == 0) {
        u2f.getMessagePort(function(port) {
          u2f.port_ = port;
          u2f.port_.addEventListener('message',
              /** @type {function(Event)} */ (u2f.responseHandler_));

          // Careful, here be async callbacks. Maybe.
          while (u2f.waitingForPort_.length)
            u2f.waitingForPort_.shift()(u2f.port_);
        });
      }
      u2f.waitingForPort_.push(callback);
    }
  };

  /**
   * Handles response messages from the extension.
   * @param {MessageEvent.<u2f.Response>} message
   * @private
   */
  u2f.responseHandler_ = function(message) {
    var response = message.data;
    var reqId = response['requestId'];
    if (!reqId || !u2f.callbackMap_[reqId]) {
      console.error('Unknown or missing requestId in response.');
      return;
    }
    var cb = u2f.callbackMap_[reqId];
    delete u2f.callbackMap_[reqId];
    cb(response['responseData']);
  };

  /**
   * Calls the callback with true or false as first and only argument
   * @param {Function} callback
   */
  u2f.isSupported = function(callback) {
    var hasCalledBack = false;
    function reply(value) {
      if (hasCalledBack)
        return;
      hasCalledBack = true;
      callback(value);
    }
    u2f.getApiVersion(
        function (response) {
          js_api_version = response['js_api_version'] === undefined ? 0 : response['js_api_version'];
          reply(true);
        }
    );
    // No response from extension within 1500ms -> no support
    setTimeout(reply.bind(null, false), 1500);
  };

  /**
   * Dispatches an array of sign requests to available U2F tokens.
   * If the JS API version supported by the extension is unknown, it first sends a
   * message to the extension to find out the supported API version and then it sends
   * the sign request.
   * @param {string=} appId
   * @param {string=} challenge
   * @param {Array<u2f.RegisteredKey>} registeredKeys
   * @param {function((u2f.Error|u2f.SignResponse))} callback
   * @param {number=} opt_timeoutSeconds
   */
  u2f.sign = function(appId, challenge, registeredKeys, callback, opt_timeoutSeconds) {
    if (js_api_version === undefined) {
      // Send a message to get the extension to JS API version, then send the actual sign request.
      u2f.getApiVersion(
          function (response) {
            js_api_version = response['js_api_version'] === undefined ? 0 : response['js_api_version'];
            console.log("Extension JS API Version: ", js_api_version);
            u2f.sendSignRequest(appId, challenge, registeredKeys, callback, opt_timeoutSeconds);
          });
    } else {
      // We know the JS API version. Send the actual sign request in the supported API version.
      u2f.sendSignRequest(appId, challenge, registeredKeys, callback, opt_timeoutSeconds);
    }
  };

  /**
   * Dispatches an array of sign requests to available U2F tokens.
   * @param {string=} appId
   * @param {string=} challenge
   * @param {Array<u2f.RegisteredKey>} registeredKeys
   * @param {function((u2f.Error|u2f.SignResponse))} callback
   * @param {number=} opt_timeoutSeconds
   */
  u2f.sendSignRequest = function(appId, challenge, registeredKeys, callback, opt_timeoutSeconds) {
    u2f.getPortSingleton_(function(port) {
      var reqId = ++u2f.reqCounter_;
      u2f.callbackMap_[reqId] = callback;
      var timeoutSeconds = (typeof opt_timeoutSeconds !== 'undefined' ?
          opt_timeoutSeconds : u2f.EXTENSION_TIMEOUT_SEC);
      var req = u2f.formatSignRequest_(appId, challenge, registeredKeys, timeoutSeconds, reqId);
      port.postMessage(req);
    });
  };

  /**
   * Dispatches register requests to available U2F tokens. An array of sign
   * requests identifies already registered tokens.
   * If the JS API version supported by the extension is unknown, it first sends a
   * message to the extension to find out the supported API version and then it sends
   * the register request.
   * @param {string=} appId
   * @param {Array<u2f.RegisterRequest>} registerRequests
   * @param {Array<u2f.RegisteredKey>} registeredKeys
   * @param {function((u2f.Error|u2f.RegisterResponse))} callback
   * @param {number=} opt_timeoutSeconds
   */
  u2f.register = function(appId, registerRequests, registeredKeys, callback, opt_timeoutSeconds) {
    if (js_api_version === undefined) {
      // Send a message to get the extension to JS API version, then send the actual register request.
      u2f.getApiVersion(
          function (response) {
            js_api_version = response['js_api_version'] === undefined ? 0: response['js_api_version'];
            console.log("Extension JS API Version: ", js_api_version);
            u2f.sendRegisterRequest(appId, registerRequests, registeredKeys,
                callback, opt_timeoutSeconds);
          });
    } else {
      // We know the JS API version. Send the actual register request in the supported API version.
      u2f.sendRegisterRequest(appId, registerRequests, registeredKeys,
          callback, opt_timeoutSeconds);
    }
  };

  /**
   * Dispatches register requests to available U2F tokens. An array of sign
   * requests identifies already registered tokens.
   * @param {string=} appId
   * @param {Array<u2f.RegisterRequest>} registerRequests
   * @param {Array<u2f.RegisteredKey>} registeredKeys
   * @param {function((u2f.Error|u2f.RegisterResponse))} callback
   * @param {number=} opt_timeoutSeconds
   */
  u2f.sendRegisterRequest = function(appId, registerRequests, registeredKeys, callback, opt_timeoutSeconds) {
    u2f.getPortSingleton_(function(port) {
      var reqId = ++u2f.reqCounter_;
      u2f.callbackMap_[reqId] = callback;
      var timeoutSeconds = (typeof opt_timeoutSeconds !== 'undefined' ?
          opt_timeoutSeconds : u2f.EXTENSION_TIMEOUT_SEC);
      var req = u2f.formatRegisterRequest_(
          appId, registeredKeys, registerRequests, timeoutSeconds, reqId);
      port.postMessage(req);
    });
  };


  /**
   * Dispatches a message to the extension to find out the supported
   * JS API version.
   * If the user is on a mobile phone and is thus using Google Authenticator instead
   * of the Chrome extension, don't send the request and simply return 0.
   * @param {function((u2f.Error|u2f.GetJsApiVersionResponse))} callback
   * @param {number=} opt_timeoutSeconds
   */
  u2f.getApiVersion = function(callback, opt_timeoutSeconds) {
    u2f.getPortSingleton_(function(port) {
      // If we are using Android Google Authenticator or iOS client app,
      // do not fire an intent to ask which JS API version to use.
      if (port.getPortType) {
        var apiVersion;
        switch (port.getPortType()) {
          case 'WrappedIosPort_':
          case 'WrappedAuthenticatorPort_':
            apiVersion = 1.1;
            break;

          default:
            apiVersion = 0;
            break;
        }
        callback({ 'js_api_version': apiVersion });
        return;
      }
      var reqId = ++u2f.reqCounter_;
      u2f.callbackMap_[reqId] = callback;
      var req = {
        type: u2f.MessageTypes.U2F_GET_API_VERSION_REQUEST,
        timeoutSeconds: (typeof opt_timeoutSeconds !== 'undefined' ?
            opt_timeoutSeconds : u2f.EXTENSION_TIMEOUT_SEC),
        requestId: reqId
      };
      port.postMessage(req);
    });
  };

  // Feature detection (yes really)
  // For IE and Edge detection, see https://stackoverflow.com/questions/31757852#31757969
  // and https://stackoverflow.com/questions/56360225#56361977
  var isBrowser = (typeof navigator !== 'undefined') && !!navigator.userAgent;
  var isSafari = isBrowser && navigator.userAgent.match(/Safari\//)
      && !navigator.userAgent.match(/Chrome\//);
  var isEDGE = isBrowser && /(Edge\/)|(edg\/)/i.test(navigator.userAgent);
  var isIE = isBrowser && /(MSIE 9|MSIE 10|rv:11.0)/i.test(navigator.userAgent);
  var _backend = null;
  function getBackend() {
    if (_backend)
      return _backend;
    var supportChecker = new Promise(function (resolve, reject) {
      function notSupported() {
        resolve({ u2f: null });
      }
      if (!isBrowser)
        return notSupported();
      if (isSafari)
          // Safari doesn't support U2F, and the Safari-FIDO-U2F
          // extension lacks full support (Multi-facet apps), so we
          // block it until proper support.
        return notSupported();
      var hasNativeSupport = (typeof window.u2f !== 'undefined') &&
          (typeof window.u2f.sign === 'function');
      if (hasNativeSupport)
        return resolve({ u2f: window.u2f });
      if (isEDGE || isIE)
          // We don't want to check for Google's extension hack on EDGE & IE
          // as it'll cause trouble (popups, etc)
        return notSupported();
      if (location.protocol === 'http:')
          // U2F isn't supported over http, only https
        return notSupported();
      if (typeof MessageChannel === 'undefined')
          // Unsupported browser, the chrome hack would throw
        return notSupported();
      // Test for google extension support
      chromeApi.isSupported(function (ok) {
        if (ok)
          resolve({ u2f: chromeApi });
        else
          notSupported();
      });
    })
        .then(function (response) {
          _backend = response.u2f ? supportChecker : null;
          return response;
        });
    return supportChecker;
  }
  var ErrorCodes = {
    OK: 0,
    OTHER_ERROR: 1,
    BAD_REQUEST: 2,
    CONFIGURATION_UNSUPPORTED: 3,
    DEVICE_INELIGIBLE: 4,
    TIMEOUT: 5
  };
  var ErrorNames = {
    "0": "OK",
    "1": "OTHER_ERROR",
    "2": "BAD_REQUEST",
    "3": "CONFIGURATION_UNSUPPORTED",
    "4": "DEVICE_INELIGIBLE",
    "5": "TIMEOUT"
  };
  function makeError(msg, err) {
    var code = err != null ? err.errorCode : 1; // Default to OTHER_ERROR
    var type = ErrorNames[('' + code)];
    var error = new Error(msg);
    error.metaData = { type: type, code: code };
    return error;
  }
  function isSupported() {
    return getBackend()
        .then(function (backend) { return !!backend.u2f; });
  }
  function _ensureSupport(backend) {
    if (!backend.u2f) {
      if (location.protocol === 'http:')
        throw new Error("U2F isn't supported over http, only https");
      throw new Error("U2F not supported");
    }
  }
  function ensureSupport() {
    return getBackend()
        .then(_ensureSupport);
  }
  function arrayify(value) {
    if (value != null && Array.isArray(value))
      return value;
    return value == null
        ? []
        : Array.isArray(value)
            ? value.slice() : [value];
  }
  function register(registerRequests, signRequests, timeout) {
    var _registerRequests = arrayify(registerRequests);
    if (typeof signRequests === 'number' && typeof timeout === 'undefined') {
      timeout = signRequests;
      signRequests = [];
    }
    var _signRequests = arrayify(signRequests);
    return getBackend()
        .then(function (backend) {
          _ensureSupport(backend);
          var u2f = backend.u2f;
          return new Promise(function (resolve, reject) {
            function callback(response) {
              if (response.errorCode)
                reject(makeError("Registration failed", response));
              else {
                delete response.errorCode;
                resolve(response);
              }
            }
            var appId = _registerRequests[0].appId;
            u2f.register(appId, _registerRequests, _signRequests, callback, timeout);
          });
        });
  }
  function sign(signRequests, timeout) {
    var _signRequests = arrayify(signRequests);
    return getBackend()
        .then(function (backend) {
          _ensureSupport(backend);
          var u2f = backend.u2f;
          return new Promise(function (resolve, reject) {
            var _a;
            function callback(response) {
              if (response.errorCode)
                reject(makeError("Sign failed", response));
              else {
                delete response.errorCode;
                resolve(response);
              }
            }
            var appId = _signRequests[0].appId;
            var challenge = _signRequests[0].challenge;
            var registeredKeys = (_a = []).concat.apply(_a, _signRequests
                .map(function (_a) {
                  var version = _a.version, keyHandle = _a.keyHandle, appId = _a.appId;
                  return arrayify(keyHandle)
                      .map(function (keyHandle) {
                        return ({ version: version, keyHandle: keyHandle, appId: appId });
                      });
                }));
            u2f.sign(appId, challenge, registeredKeys, callback, timeout);
          });
        });
  }

  var u2fApi = /*#__PURE__*/Object.freeze({
    ErrorCodes: ErrorCodes,
    ErrorNames: ErrorNames,
    isSupported: isSupported,
    ensureSupport: ensureSupport,
    register: register,
    sign: sign
  });

  window.u2fApi = u2fApi;
}());
