﻿namespace formulate.app.Controllers
{

    // Namespaces.
    using Helpers;
    using Layouts;
    using Layouts.Kinds.Basic;
    using Models.Requests;
    using Persistence;
    using System;
    using System.Linq;
    using System.Web.Http;
    using Umbraco.Core;
    using Umbraco.Core.Logging;
    using Umbraco.Web.Editors;
    using Umbraco.Web.Mvc;
    using Umbraco.Web.WebApi.Filters;
    using CoreConstants = Umbraco.Core.Constants;
    using LayoutBasicConstants = formulate.app.Constants.Layouts.LayoutBasic;
    using LayoutConstants = formulate.app.Constants.Trees.Layouts;


    /// <summary>
    /// Controller for Formulate layouts.
    /// </summary>
    [PluginController("formulate")]
    [UmbracoApplicationAuthorize("formulate")]
    public class LayoutsController : UmbracoAuthorizedJsonController
    {

        #region Constants

        private const string UnhandledError = @"An unhandled error occurred. Refer to the error log.";
        private const string PersistLayoutError = @"An error occurred while attempting to persist a Formulate layout.";
        private const string GetLayoutInfoError = @"An error occurred while attempting to get the layout info for a Formulate layout.";
        private const string DeleteLayoutError = @"An error occurred while attempting to delete the Formulate layout.";
        private const string GetKindsError = @"An error occurred while attempting to get the layout kinds.";
        private const string MoveLayoutError = @"An error occurred while attempting to move a Formulate layout.";

        #endregion


        #region Properties

        private ILayoutPersistence Persistence { get; set; }
        private IFormPersistence FormPersistence { get; set; }
        private IEntityPersistence Entities { get; set; }

        #endregion


        #region Constructors

        /// <summary>
        /// Default constructor.
        /// </summary>
        public LayoutsController(ILayoutPersistence layoutPersistence, IFormPersistence formPersistence,
            IEntityPersistence entityPersistence)
        {
            Persistence = layoutPersistence;
            FormPersistence = formPersistence;
            Entities = entityPersistence;
        }

        #endregion


        #region Web Methods

        /// <summary>
        /// Creates a layout.
        /// </summary>
        /// <param name="request">
        /// The request to create the layout.
        /// </param>
        /// <returns>
        /// An object indicating success or failure, along with some
        /// accompanying data.
        /// </returns>
        [HttpPost]
        public object PersistLayout(PersistLayoutRequest request)
        {

            // Variables.
            var result = default(object);
            var rootId = CoreConstants.System.Root.ToInvariantString();
            var layoutsRootId = GuidHelper.GetGuid(LayoutConstants.Id);
            var parentId = GuidHelper.GetGuid(request.ParentId);
            var kindId = GuidHelper.GetGuid(request.KindId);


            // Catch all errors.
            try
            {

                // Parse or create the layout ID.
                var layoutId = string.IsNullOrWhiteSpace(request.LayoutId)
                    ? Guid.NewGuid()
                    : GuidHelper.GetGuid(request.LayoutId);


                // Get the ID path.
                var parent = parentId == Guid.Empty ? null : Entities.Retrieve(parentId);
                var path = parent == null
                    ? new[] { layoutsRootId, layoutId }
                    : parent.Path.Concat(new[] { layoutId }).ToArray();


                // Create layout.
                var serializedData = JsonHelper.Serialize(request.Data);
                var layout = new Layout()
                {
                    KindId = kindId,
                    Id = layoutId,
                    Path = path,
                    Name = request.LayoutName,
                    Alias = request.LayoutAlias,
                    Data = serializedData
                };


                // Automatically populate the layout based on the form?
                var isBasic = GuidHelper.GetGuid(request.KindId) == GuidHelper.GetGuid(LayoutBasicConstants.Id);
                var config = isBasic
                    ? new LayoutBasic().DeserializeConfiguration(serializedData) as LayoutBasicConfiguration
                    : null;
                var shouldAutopopulate = (config?.Autopopulate).GetValueOrDefault(false);
                var hasFormId = (config?.FormId.HasValue).GetValueOrDefault(false);
                var form = hasFormId
                    ? FormPersistence.Retrieve(config.FormId.Value)
                    : null;
                if (isBasic && shouldAutopopulate && form != null)
                {
                    var autoLayoutData = JsonHelper.Serialize(new
                    {
                        rows = new[]
                        {
                            new
                            {
                                cells = new []
                                {
                                    new
                                    {
                                        columnSpan = 12,
                                        fields = form.Fields.Select(x => new
                                        {
                                            id = GuidHelper.GetString(x.Id)
                                        })
                                    }
                                }
                            }
                        },
                        formId = GuidHelper.GetString(form.Id),
                        autopopulate = true
                    });
                    layout.Data = autoLayoutData;
                }


                // Persist layout.
                Persistence.Persist(layout);


                // Variables.
                var fullPath = new[] { rootId }
                    .Concat(path.Select(x => GuidHelper.GetString(x)))
                    .ToArray();


                // Success.
                result = new
                {
                    Success = true,
                    Id = GuidHelper.GetString(layoutId),
                    Path = fullPath
                };

            }
            catch(Exception ex)
            {

                // Error.
                Logger.Error<LayoutsController>(ex, PersistLayoutError);
                result = new
                {
                    Success = false,
                    Reason = UnhandledError
                };

            }


            // Return result.
            return result;

        }


        /// <summary>
        /// Returns info about the layout with the specified ID.
        /// </summary>
        /// <param name="request">
        /// The request to get the layout info.
        /// </param>
        /// <returns>
        /// An object indicating success or failure, along with some
        /// accompanying data.
        /// </returns>
        [HttpGet]
        public object GetLayoutInfo([FromUri] GetLayoutInfoRequest request)
        {

            // Variables.
            var result = default(object);
            var rootId = CoreConstants.System.Root.ToInvariantString();


            // Catch all errors.
            try
            {

                // Variables.
                var id = GuidHelper.GetGuid(request.LayoutId);
                var layout = Persistence.Retrieve(id);
                var partialPath = layout.Path
                    .Select(x => GuidHelper.GetString(x));
                var fullPath = new[] { rootId }
                    .Concat(partialPath)
                    .ToArray();
                var kinds = GetAllLayoutKinds();
                var directive = kinds
                    .Where(x => x.Id == layout.KindId)
                    .Select(x => x.Directive).FirstOrDefault();


                // Set result.
                result = new
                {
                    Success = true,
                    LayoutId = GuidHelper.GetString(layout.Id),
                    KindId = GuidHelper.GetString(layout.KindId),
                    Path = fullPath,
                    Alias = layout.Alias,
                    Name = layout.Name,
                    Directive = directive,
                    Data = JsonHelper.Deserialize<object>(layout.Data)
                };

            }
            catch (Exception ex)
            {

                // Error.
                Logger.Error<LayoutsController>(ex, GetLayoutInfoError);
                result = new
                {
                    Success = false,
                    Reason = UnhandledError
                };

            }


            // Return result.
            return result;

        }


        /// <summary>
        /// Deletes the layout with the specified ID.
        /// </summary>
        /// <param name="request">
        /// The request to delete the layout.
        /// </param>
        /// <returns>
        /// An object indicating success or failure, along with some
        /// accompanying data.
        /// </returns>
        [HttpPost()]
        public object DeleteLayout(DeleteLayoutRequest request)
        {

            // Variables.
            var result = default(object);


            // Catch all errors.
            try
            {

                // Variables.
                var layoutId = GuidHelper.GetGuid(request.LayoutId);


                // Delete the layout.
                Persistence.Delete(layoutId);


                // Success.
                result = new
                {
                    Success = true
                };

            }
            catch (Exception ex)
            {

                // Error.
                Logger.Error<LayoutsController>(ex, DeleteLayoutError);
                result = new
                {
                    Success = false,
                    Reason = UnhandledError
                };

            }


            // Return the result.
            return result;

        }


        /// <summary>
        /// Returns the layout kinds.
        /// </summary>
        /// <returns>
        /// An object indicating success or failure, along with
        /// information about layout kinds.
        /// </returns>
        [HttpGet]
        public object GetLayoutKinds()
        {

            // Variables.
            var result = default(object);


            // Catch all errors.
            try
            {

                // Variables.
                var kinds = GetAllLayoutKinds();


                // Return results.
                result = new
                {
                    Success = true,
                    Kinds = kinds.Select(x => new
                    {
                        Id = GuidHelper.GetString(x.Id),
                        Name = x.Name,
                        Directive = x.Directive
                    }).ToArray()
                };

            }
            catch (Exception ex)
            {

                // Error.
                Logger.Error<LayoutsController>(ex, GetKindsError);
                result = new
                {
                    Success = false,
                    Reason = UnhandledError
                };

            }


            // Return result.
            return result;

        }


        /// <summary>
        /// Moves layout to a new parent.
        /// </summary>
        /// <param name="request">
        /// The request to move the layout.
        /// </param>
        /// <returns>
        /// An object indicating success or failure, along with information
        /// about the layout.
        /// </returns>
        [HttpPost]
        public object MoveLayout(MoveLayoutRequest request)
        {

            // Variables.
            var result = default(object);
            var rootId = CoreConstants.System.Root.ToInvariantString();
            var parentId = GuidHelper.GetGuid(request.NewParentId);


            // Catch all errors.
            try
            {

                // Parse the layout ID.
                var layoutId = GuidHelper.GetGuid(request.LayoutId);


                // Get the ID path.
                var path = Entities.Retrieve(parentId).Path
                    .Concat(new[] { layoutId }).ToArray();


                // Get layout and update path.
                var layout = Persistence.Retrieve(layoutId);
                layout.Path = path;


                // Persist layout.
                Persistence.Persist(layout);


                // Variables.
                var fullPath = new[] { rootId }
                    .Concat(path.Select(x => GuidHelper.GetString(x)))
                    .ToArray();


                // Success.
                result = new
                {
                    Success = true,
                    Id = GuidHelper.GetString(layoutId),
                    Path = fullPath
                };

            }
            catch (Exception ex)
            {

                // Error.
                Logger.Error<LayoutsController>(ex, MoveLayoutError);
                result = new
                {
                    Success = false,
                    Reason = UnhandledError
                };

            }


            // Return result.
            return result;

        }

        #endregion


        #region Helper Methods

        /// <summary>
        /// Returns the layout kinds.
        /// </summary>
        private ILayoutKind[] GetAllLayoutKinds()
        {
            var instances = ReflectionHelper
                .InstantiateInterfaceImplementations<ILayoutKind>();
            return instances;
        }

        #endregion

    }

}