﻿// Variables.
var app = angular.module("umbraco");

// Register directive/controller.
app.controller("formulate.removeEntityFromCloudConfirmation", controller);
app.directive("formulateRemoveEntityFromCloudConfirmation", directive);

// Directive.
function directive(formulateDirectives) {
    return {
        restrict: "E",
        template: formulateDirectives.get("removeEntityFromCloudConfirmation/removeEntityFromCloud.html"),
        controller: "formulate.removeEntityFromCloudConfirmation"
    };
}

// Controller.
function controller($scope, navigationService,
    formulateEntities, formulateCloud, notificationsService) {

    // Variables.
    var entityId = $scope.currentNode.id;

    // Variable containing the common services (easier to pass around).
    var services = {
        $scope,
        navigationService,
        formulateEntities,
        formulateCloud,
        notificationsService
    };

    // Assign scope variables.
    $scope.initialized = false;

    // Assign functions on scope.
    $scope.removeEntity = getRemoveEntity(services);
    $scope.cancel = getCancel(services);

    // Load entity information.
    loadEntityInfo(entityId, services);

}

// Loads information about the entity.
function loadEntityInfo(entityId, services) {
    services.formulateEntities.getEntity(entityId).then(function(entity) {
        services.$scope.entityId = entity.id;
        services.$scope.entityName = entity.name;
        services.$scope.initialized = true;
    });
}

// Returns a function that removes the entity from Umbraco Cloud.
function getRemoveEntity(services) {
    return function() {

        // Variables.
        var entityId = services.$scope.entityId;

        // Remove entity.
        services.formulateCloud.removeEntityFromCloud(entityId)
            .then(function () {

                // Close dialog.
                services.navigationService.hideDialog();

                // Show success notification.
                var title = "Success",
                    message = "The entity was removed from Umbraco Cloud.";
                services.notificationsService.success(title, message);

            });

    };
}

// Returns the function that cancels the storing.
function getCancel(services) {
    return function () {
        services.navigationService.hideDialog();
    };
}