
    var core = breeze.core;
    
    var jQuery;
    
    var ctor = function () {
        this.name = "jQuery";
        this.defaultSettings = { };
    };

    ctor.prototype.initialize = function () {
        // jQuery = core.requireLib("jQuery", "needed for 'ajax_jQuery' pluggin", true);
        // for the time being don't fail if not found
        jQuery = core.requireLib("jQuery");
    };

    ctor.prototype.ajax = function (settings) {
        if (!jQuery) {
            throw new Error("Unable to locate jQuery");
        }
        if (! core.isEmpty(this.defaultSettings)) {
            var compositeSettings = core.extend({}, this.defaultSettings);
            core.extend(compositeSettings, settings);
            jQuery.ajax(compositeSettings);
        } else {
            jQuery.ajax(settings);
        }
    };

    
    // last param is true because for now we only have one impl.
    breeze.config.registerAdapter("ajax", ctor);
    

