﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Core;
using Umbraco.Core.Configuration.Models;
using Umbraco.Core.Dictionary;
using Umbraco.Core.Events;
using Umbraco.Core.Mapping;
using Umbraco.Core.Members;
using Umbraco.Core.Models;
using Umbraco.Core.Models.ContentEditing;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Security;
using Umbraco.Core.Serialization;
using Umbraco.Core.Services;
using Umbraco.Core.Services.Implement;
using Umbraco.Core.Strings;
using Umbraco.Extensions;
using Umbraco.Infrastructure.Members;
using Umbraco.Web.BackOffice.Filters;
using Umbraco.Web.BackOffice.ModelBinders;
using Umbraco.Web.Common.Attributes;
using Umbraco.Web.Common.Authorization;
using Umbraco.Web.Common.Exceptions;
using Umbraco.Web.Common.Filters;
using Umbraco.Web.ContentApps;
using Umbraco.Web.Models.ContentEditing;
using Constants = Umbraco.Core.Constants;

namespace Umbraco.Web.BackOffice.Controllers
{
    /// <remarks>
    /// This controller is decorated with the UmbracoApplicationAuthorizeAttribute which means that any user requesting
    /// access to ALL of the methods on this controller will need access to the member application.
    /// </remarks>
    [PluginController(Constants.Web.Mvc.BackOfficeApiArea)]
    [Authorize(Policy = AuthorizationPolicies.SectionAccessMembers)]
    [OutgoingNoHyphenGuidFormat]
    public class MemberController : ContentControllerBase
    {
        private readonly PropertyEditorCollection _propertyEditors;
        private readonly UmbracoMapper _umbracoMapper;
        private readonly IMemberService _memberService;
        private readonly IMemberTypeService _memberTypeService;
        private readonly IUmbracoMembersUserManager _memberManager;
        private readonly IDataTypeService _dataTypeService;
        private readonly ILocalizedTextService _localizedTextService;
        private readonly IBackOfficeSecurityAccessor _backofficeSecurityAccessor;
        private readonly IJsonSerializer _jsonSerializer;

        public MemberController(
            ICultureDictionary cultureDictionary,
            ILoggerFactory loggerFactory,
            IShortStringHelper shortStringHelper,
            IEventMessagesFactory eventMessages,
            ILocalizedTextService localizedTextService,
            PropertyEditorCollection propertyEditors,
            UmbracoMapper umbracoMapper,
            IMemberService memberService,
            IMemberTypeService memberTypeService,
            IUmbracoMembersUserManager memberManager,
            IDataTypeService dataTypeService,
            IBackOfficeSecurityAccessor backofficeSecurityAccessor,
            IJsonSerializer jsonSerializer)
            : base(cultureDictionary, loggerFactory, shortStringHelper, eventMessages, localizedTextService, jsonSerializer)
        {
            _propertyEditors = propertyEditors;
            _umbracoMapper = umbracoMapper;
            _memberService = memberService;
            _memberTypeService = memberTypeService;
            _memberManager = memberManager;
            _dataTypeService = dataTypeService;
            _localizedTextService = localizedTextService;
            _backofficeSecurityAccessor = backofficeSecurityAccessor;
            _jsonSerializer = jsonSerializer;
        }

        public PagedResult<MemberBasic> GetPagedResults(
            int pageNumber = 1,
            int pageSize = 100,
            string orderBy = "username",
            Direction orderDirection = Direction.Ascending,
            bool orderBySystemField = true,
            string filter = "",
            string memberTypeAlias = null)
        {

            if (pageNumber <= 0 || pageSize <= 0)
            {
                throw new NotSupportedException("Both pageNumber and pageSize must be greater than zero");
            }

            IMember[] members = _memberService.GetAll(
                pageNumber - 1,
                pageSize,
                out var totalRecords,
                orderBy,
                orderDirection,
                orderBySystemField,
                memberTypeAlias,
                filter).ToArray();
            if (totalRecords == 0)
            {
                return new PagedResult<MemberBasic>(0, 0, 0);
            }

            var pagedResult = new PagedResult<MemberBasic>(totalRecords, pageNumber, pageSize)
            {
                Items = members.Select(x => _umbracoMapper.Map<MemberBasic>(x))
            };
            return pagedResult;
        }

        /// <summary>
        /// Returns a display node with a list view to render members
        /// </summary>
        /// <param name="listName"></param>
        /// <returns></returns>
        public MemberListDisplay GetListNodeDisplay(string listName)
        {
            var foundType = _memberTypeService.Get(listName);
            var name = foundType != null ? foundType.Name : listName;

            var apps = new List<ContentApp>
            {
                ListViewContentAppFactory.CreateContentApp(
                    _dataTypeService,
                    _propertyEditors,
                    listName,
                    Constants.Security.DefaultMemberTypeAlias.ToLower(),
                    Constants.DataTypes.DefaultMembersListView)
            };
            apps[0].Active = true;

            var display = new MemberListDisplay
            {
                ContentTypeAlias = listName,
                ContentTypeName = name,
                Id = listName,
                IsContainer = true,
                Name = listName == Constants.Conventions.MemberTypes.AllMembersListId ? "All Members" : name,
                Path = "-1," + listName,
                ParentId = -1,
                ContentApps = apps
            };

            return display;
        }

        /// <summary>
        /// Gets the content json for the member
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        [TypeFilter(typeof(OutgoingEditorModelEventAttribute))]
        public MemberDisplay GetByKey(Guid key)
        {
            IMember foundMember = _memberService.GetByKey(key);
            if (foundMember == null)
            {
                HandleContentNotFound(key);
            }
            return _umbracoMapper.Map<MemberDisplay>(foundMember);
        }

        /// <summary>
        /// Gets an empty content item for the
        /// </summary>
        /// <param name="contentTypeAlias"></param>
        /// <returns></returns>
        [TypeFilter(typeof(OutgoingEditorModelEventAttribute))]
        public MemberDisplay GetEmpty(string contentTypeAlias = null)
        {
            if (contentTypeAlias == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            IMemberType contentType = _memberTypeService.Get(contentTypeAlias);
            if (contentType == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            IMember emptyContent = new Member(contentType);

            string newPassword = _memberManager.GeneratePassword();
            emptyContent.AdditionalData["NewPassword"] = newPassword;
            return _umbracoMapper.Map<MemberDisplay>(emptyContent);
        }

        /// <summary>
        /// Saves member
        /// </summary>
        /// <returns></returns>
        [FileUploadCleanupFilter]
        [TypeFilter(typeof(OutgoingEditorModelEventAttribute))]
        [MemberSaveValidation]
        public async Task<ActionResult<MemberDisplay>> PostSave(
            [ModelBinder(typeof(MemberBinder))]
                MemberSave contentItem)
        {

            //If we've reached here it means:
            // * Our model has been bound
            // * and validated
            // * any file attachments have been saved to their temporary location for us to use
            // * we have a reference to the DTO object and the persisted object
            // * Permissions are valid

            //map the properties to the persisted entity
            MapPropertyValues(contentItem);

            await ValidateMemberDataAsync(contentItem);

            //Unlike content/media - if there are errors for a member, we do NOT proceed to save them, we cannot so return the errors
            if (ModelState.IsValid == false)
            {
                var forDisplay = _umbracoMapper.Map<MemberDisplay>(contentItem.PersistedContent);
                forDisplay.Errors = ModelState.ToErrorDictionary();
                throw HttpResponseException.CreateValidationErrorResponse(forDisplay);
            }

            //We're gonna look up the current roles now because the below code can cause
            // events to be raised and developers could be manually adding roles to members in
            // their handlers. If we don't look this up now there's a chance we'll just end up
            // removing the roles they've assigned.
            var currRoles = _memberService.GetAllRoles(contentItem.PersistedContent.Username);

            //find the ones to remove and remove them
            IEnumerable<string> roles = currRoles.ToList();
            var rolesToRemove = roles.Except(contentItem.Groups).ToArray();

            //Depending on the action we need to first do a create or update using the membership provider
            // this ensures that passwords are formatted correctly and also performs the validation on the provider itself.
            switch (contentItem.Action)
            {
                case ContentSaveAction.Save:
                    UpdateMemberData(contentItem);
                    break;
                case ContentSaveAction.SaveNew:
                    contentItem.PersistedContent = await CreateMemberData(contentItem);
                    break;
                default:
                    //we don't support anything else for members
                    throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            //TODO: There's 3 things saved here and we should do this all in one transaction, which we can do here by wrapping in a scope
            // but it would be nicer to have this taken care of within the Save method itself

            //create/save the IMember
            _memberService.Save(contentItem.PersistedContent);

            //Now let's do the role provider stuff - now that we've saved the content item (that is important since
            // if we are changing the username, it must be persisted before looking up the member roles).
            if (rolesToRemove.Any())
            {
                _memberService.DissociateRoles(new[] { contentItem.PersistedContent.Username }, rolesToRemove);
            }
            //find the ones to add and add them
            string[] toAdd = contentItem.Groups.Except(roles).ToArray();
            if (toAdd.Any())
            {
                //add the ones submitted
                _memberService.AssignRoles(new[] { contentItem.PersistedContent.Username }, toAdd);
            }

            //return the updated model
            MemberDisplay display = _umbracoMapper.Map<MemberDisplay>(contentItem.PersistedContent);

            //lastly, if it is not valid, add the model state to the outgoing object and throw a 403
            HandleInvalidModelState(display);

            ILocalizedTextService localizedTextService = _localizedTextService;
            //put the correct messages in
            switch (contentItem.Action)
            {
                case ContentSaveAction.Save:
                case ContentSaveAction.SaveNew:
                    display.AddSuccessNotification(
                        localizedTextService.Localize("speechBubbles/editMemberSaved"),
                        localizedTextService.Localize("speechBubbles/editMemberSaved"));
                    break;
            }

            return display;
        }

        /// <summary>
        /// Maps the property values to the persisted entity
        /// </summary>
        /// <param name="contentItem"></param>
        private void MapPropertyValues(MemberSave contentItem)
        {
            UpdateName(contentItem);

            //map the custom properties - this will already be set for new entities in our member binder
            contentItem.PersistedContent.Email = contentItem.Email;
            contentItem.PersistedContent.Username = contentItem.Username;

            //use the base method to map the rest of the properties
            base.MapPropertyValuesForPersistence<IMember, MemberSave>(
                contentItem,
                contentItem.PropertyCollectionDto,
                (save, property) => property.GetValue(), //get prop val
                (save, property, v) => property.SetValue(v), //set prop val
                null); // member are all invariant
        }

        /// <summary>
        /// Create a member from the supplied member content data
        /// All member password processing and creation is done via the aspnet identity MemberUserManager
        /// </summary>
        /// <param name="memberSave"></param>
        /// <returns></returns>
        private async Task<IMember> CreateMemberData(MemberSave memberSave)
        {
            if (memberSave == null) throw new ArgumentNullException("memberSave");

            if (ModelState.IsValid == false)
            {
                throw new HttpResponseException(HttpStatusCode.BadRequest, ModelState);
            }
            //TODO: check if unique

            IMemberType memberType = _memberTypeService.Get(memberSave.ContentTypeAlias);
            if (memberType == null)
            {
                throw new InvalidOperationException($"No member type found with alias {memberSave.ContentTypeAlias}");
            }

            // Create the member with the UserManager
            // The 'empty' (special) password format is applied without us having to duplicate that logic
            UmbracoMembersIdentityUser identityMember = UmbracoMembersIdentityUser.CreateNew(
                memberSave.Username,
                memberSave.Email,
                memberSave.Name);

            //TODO: confirm
            identityMember.MemberTypeAlias = memberType.Alias;

            IdentityResult created = await _memberManager.CreateAsync(identityMember);
            if (created.Succeeded == false)
            {
                throw HttpResponseException.CreateNotificationValidationErrorResponse(created.Errors.ToErrorMessage());
            }

            //string resetPassword;
            //string password = _memberManager.GeneratePassword();

            //IdentityResult result = await _memberManager.AddPasswordAsync(identityMember, password);
            //if (result.Succeeded == false)
            //{
            //    throw HttpResponseException.CreateNotificationValidationErrorResponse(created.Errors.ToErrorMessage());
            //}

            //resetPassword = password;

            //now re-look the member back up which will now exist
            IMember member = _memberService.GetByEmail(memberSave.Email);

            //TODO: previous implementation
            //IMember member = new Member(
            //    memberSave.Name,
            //    memberSave.Email,
            //    memberSave.Username,
            //    memberType,
            //    true)
            //{
            //    CreatorId = _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser.Id,
            //    RawPasswordValue = _memberManager.GeneratePassword(),
            //    Comments = memberSave.Comments,
            //    IsApproved = memberSave.IsApproved
            //};


            //since the back office user is creating this member, they will be set to approved
            member.IsApproved = true;
            member.CreatorId = _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser.Id;
            member.Comments = memberSave.Comments;
            member.IsApproved = memberSave.IsApproved;

            //map the save info over onto the user
            member = _umbracoMapper.Map(memberSave, member);

            _memberService.Save(member);

            return member;
        }

        /// <summary>
        /// Update the member security data
        /// </summary>
        /// <param name="memberSave"></param>
        /// <returns>
        /// If the password has been reset then this method will return the reset/generated password, otherwise will return null.
        /// </returns>
        private void UpdateMemberData(MemberSave memberSave)
        {
            //TODO: optimise based on new member manager
            memberSave.PersistedContent.WriterId = _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser.Id;

            // If the user doesn't have access to sensitive values, then we need to check if any of the built in member property types
            // have been marked as sensitive. If that is the case we cannot change these persisted values no matter what value has been posted.
            // There's only 3 special ones we need to deal with that are part of the MemberSave instance: Comments, IsApproved, IsLockedOut
            // but we will take care of this in a generic way below so that it works for all props.
            if (!_backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser.HasAccessToSensitiveData())
            {
                var memberType = _memberTypeService.Get(memberSave.PersistedContent.ContentTypeId);
                var sensitiveProperties = memberType
                    .PropertyTypes.Where(x => memberType.IsSensitiveProperty(x.Alias))
                    .ToList();

                foreach (var sensitiveProperty in sensitiveProperties)
                {
                    var destProp = memberSave.Properties.FirstOrDefault(x => x.Alias == sensitiveProperty.Alias);
                    if (destProp != null)
                    {
                        //if found, change the value of the contentItem model to the persisted value so it remains unchanged
                        var origValue = memberSave.PersistedContent.GetValue(sensitiveProperty.Alias);
                        destProp.Value = origValue;
                    }
                }
            }

            var isLockedOut = memberSave.IsLockedOut;

            //if they were locked but now they are trying to be unlocked
            if (memberSave.PersistedContent.IsLockedOut && isLockedOut == false)
            {
                memberSave.PersistedContent.IsLockedOut = false;
                memberSave.PersistedContent.FailedPasswordAttempts = 0;
            }
            else if (!memberSave.PersistedContent.IsLockedOut && isLockedOut)
            {
                //NOTE: This should not ever happen unless someone is mucking around with the request data.
                //An admin cannot simply lock a user, they get locked out by password attempts, but an admin can un-approve them
                ModelState.AddModelError("custom", "An admin cannot lock a user");
            }

            //no password changes then exit ?
            if (memberSave.Password == null)
                return;

            // set the password
            memberSave.PersistedContent.RawPasswordValue = _memberManager.GeneratePassword();
        }

        private static void UpdateName(MemberSave memberSave)
        {
            //Don't update the name if it is empty
            if (memberSave.Name.IsNullOrWhiteSpace() == false)
            {
                memberSave.PersistedContent.Name = memberSave.Name;
            }
        }

        // TODO: This logic should be pulled into the service layer
        private async Task<bool> ValidateMemberDataAsync(MemberSave contentItem)
        {
            if (contentItem.Name.IsNullOrWhiteSpace())
            {
                ModelState.AddPropertyError(
                    new ValidationResult("Invalid user name", new[] { "value" }),
                    $"{Constants.PropertyEditors.InternalGenericPropertiesPrefix}login");
                return false;
            }

            if (contentItem.Password != null && !contentItem.Password.NewPassword.IsNullOrWhiteSpace())
            {
                //TODO: implement as per backoffice user
                //var validPassword = await _memberManager.CheckPasswordAsync(null, contentItem.Password.NewPassword);
                //if (!validPassword)
                //{
                //    ModelState.AddPropertyError(
                //       new ValidationResult("Invalid password: TODO", new[] { "value" }),
                //       $"{Constants.PropertyEditors.InternalGenericPropertiesPrefix}password");
                //    return false;
                //}
                return true;
            }

            var byUsername = _memberService.GetByUsername(contentItem.Username);
            if (byUsername != null && byUsername.Key != contentItem.Key)
            {
                ModelState.AddPropertyError(
                        new ValidationResult("Username is already in use", new[] { "value" }),
                        $"{Constants.PropertyEditors.InternalGenericPropertiesPrefix}login");
                return false;
            }

            var byEmail = _memberService.GetByEmail(contentItem.Email);
            if (byEmail != null && byEmail.Key != contentItem.Key)
            {
                ModelState.AddPropertyError(
                        new ValidationResult("Email address is already in use", new[] { "value" }),
                        $"{Constants.PropertyEditors.InternalGenericPropertiesPrefix}email");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Permanently deletes a member
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        ///
        [HttpPost]
        public IActionResult DeleteByKey(Guid key)
        {
            var foundMember = _memberService.GetByKey(key);
            if (foundMember == null)
            {
                return HandleContentNotFound(key, false);
            }
            _memberService.Delete(foundMember);

            return Ok();
        }

        /// <summary>
        /// Exports member data based on their unique Id
        /// </summary>
        /// <param name="key">The unique <see cref="Guid">member identifier</see></param>
        /// <returns><see cref="HttpResponseMessage"/></returns>
        [HttpGet]
        public IActionResult ExportMemberData(Guid key)
        {
            var currentUser = _backofficeSecurityAccessor.BackOfficeSecurity.CurrentUser;

            if (currentUser.HasAccessToSensitiveData() == false)
            {
                return Forbid();
            }

            MemberExportModel member = ((MemberService)_memberService).ExportMember(key);
            if (member is null) throw new NullReferenceException("No member found with key " + key);

            var json = _jsonSerializer.Serialize(member);

            var fileName = $"{member.Name}_{member.Email}.txt";
            // Set custom header so umbRequestHelper.downloadFile can save the correct filename
            HttpContext.Response.Headers.Add("x-filename", fileName);

            return File(Encoding.UTF8.GetBytes(json), MediaTypeNames.Application.Octet, fileName);
        }
    }
}
