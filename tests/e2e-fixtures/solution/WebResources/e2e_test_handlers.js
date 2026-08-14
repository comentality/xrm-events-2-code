// E2E test handlers for Events2Code end-to-end tests.
// Each function logs its invocation so tests can assert runtime behavior.
var E2ETest = E2ETest || {};

E2ETest._log = function (name, args) {
    console.log("[E2ETest] " + name, args);
    window._e2eCalls = window._e2eCalls || [];
    window._e2eCalls.push({ fn: name, args: Array.prototype.slice.call(args) });
};

E2ETest.onFormLoad = function (executionContext) {
    E2ETest._log("onFormLoad", arguments);
};

E2ETest.onFormLoadExtra = function (arg1, arg2) {
    E2ETest._log("onFormLoadExtra", arguments);
};

E2ETest.onSave = function (executionContext) {
    E2ETest._log("onSave", arguments);
};

E2ETest.onFirstNameChange = function (executionContext) {
    E2ETest._log("onFirstNameChange", arguments);
};

E2ETest.onLastNameChange = function (executionContext) {
    E2ETest._log("onLastNameChange", arguments);
};

E2ETest.onDetailsTabStateChange = function () {
    E2ETest._log("onDetailsTabStateChange", arguments);
};

E2ETest.onSubgridLoad = function (executionContext) {
    E2ETest._log("onSubgridLoad", arguments);
};
