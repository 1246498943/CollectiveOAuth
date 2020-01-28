﻿using Come.CollectiveOAuth.Cache;
using Come.CollectiveOAuth.Config;
using Come.CollectiveOAuth.Models;
using Come.CollectiveOAuth.Utils;
using System;
using System.Collections.Generic;
using Come.CollectiveOAuth.Enums;

namespace Come.CollectiveOAuth.Request
{
    public class LinkedInAuthRequest : DefaultAuthRequest
    {
        public LinkedInAuthRequest(ClientConfig config) : base(config, new LinkedInAuthSource())
        {
        }

        public LinkedInAuthRequest(ClientConfig config, IAuthStateCache authStateCache)
            : base(config, new LinkedInAuthSource(), authStateCache)
        {
        }

        protected override AuthToken getAccessToken(AuthCallback authCallback)
        {
            return this.getToken(accessTokenUrl(authCallback.code));
        }

        protected override AuthUser getUserInfo(AuthToken authToken)
        {
            var accessToken = authToken.accessToken;
            var reqParams = new Dictionary<string, object>();
            reqParams.Add("Host", "api.linkedin.com");
            reqParams.Add("Connection", "Keep-Alive");
            reqParams.Add("Authorization", "Bearer " + accessToken);
            var response = HttpUtils.RequestGet(userInfoUrl(authToken), reqParams);
             
            var userObj = response.parseObject();

            this.checkResponse(userObj);

            string userName = getUserName(userObj);

            // 获取用户头像
            string avatar = this.getAvatar(userObj);

            // 获取用户邮箱地址
            string email = this.getUserEmail(accessToken);


            var authUser = new AuthUser();
            authUser.uuid = userObj.GetParamString("id");
            authUser.username = userName;
            authUser.nickname = userName;
            authUser.avatar = avatar;
            authUser.email = email;
            authUser.gender = AuthUserGender.UNKNOWN;

            authUser.token = authToken;
            authUser.source = source.getName();
            authUser.originalUser = userObj;
            authUser.originalUserStr = response;
            return authUser;
        }

        /**
         * 获取用户的真实名
         *
         * @param userInfoObject 用户json对象
         * @return 用户名
         */
        private string getUserName(Dictionary<string, object> userInfoObject)
        {
            string firstName, lastName;
            // 获取firstName
            if (userInfoObject.ContainsKey("localizedFirstName"))
            {
                firstName = userInfoObject.GetParamString("localizedFirstName");
            }
            else
            {
                firstName = getUserName(userInfoObject, "firstName");
            }
            // 获取lastName
            if (userInfoObject.ContainsKey("localizedLastName"))
            {
                lastName = userInfoObject.GetParamString("localizedLastName");
            }
            else
            {
                lastName = getUserName(userInfoObject, "lastName");
            }
            return firstName + " " + lastName;
        }

        /**
         * 获取用户的头像
         *
         * @param userInfoObject 用户json对象
         * @return 用户的头像地址
         */
        private string getAvatar(Dictionary<string, object> userInfoObject)
        {
            string avatar = null;
            var profilePictureObject = userInfoObject.GetParamString("profilePicture").parseObject();
            if (profilePictureObject.ContainsKey("displayImage~"))
            {
                var displayImageElements = profilePictureObject.GetParamString("displayImage~").parseObject()
                    .GetParamString("elements").parseListObject();
                if (null != displayImageElements && displayImageElements.Count > 0)
                {
                    var largestImageObj = displayImageElements[displayImageElements.Count - 1];
                    avatar = largestImageObj.GetParamString("identifiers").parseListObject()[0].GetParamString("identifier");
                }
            }
            return avatar;
        }

        /**
         * 获取用户的email
         *
         * @param accessToken 用户授权后返回的token
         * @return 用户的邮箱地址
         */
        private string getUserEmail(string accessToken)
        {
            string email = null;
            var reqParams = new Dictionary<string, object>();
            reqParams.Add("Host", "api.linkedin.com");
            reqParams.Add("Connection", "Keep-Alive");
            reqParams.Add("Authorization", "Bearer " + accessToken);
            var emailResponse = HttpUtils.RequestGet("https://api.linkedin.com/v2/emailAddress?q=members&projection=(elements*(handle~))", reqParams);
                
            var emailObj = emailResponse.parseObject();
            this.checkResponse(emailObj);
            var listObject = emailObj.GetParamString("elements").parseListObject();
            if (listObject != null && listObject.Count > 0)
            {
                email = listObject[listObject.Count - 1].GetParamString("handle~").parseObject().GetParamString("emailAddress");
            }

            return email;
        }

        private string getUserName(Dictionary<string, object> userInfoObject, string nameKey)
        {
            string firstName;
            var firstNameObj = userInfoObject.GetParamString(nameKey).parseObject();
            var localizedObj = firstNameObj.GetParamString("localized").parseObject();
            var preferredLocaleObj = firstNameObj.GetParamString("preferredLocale").parseObject();
            firstName = localizedObj.GetParamString(preferredLocaleObj.GetParamString("language") + "_" + preferredLocaleObj.GetParamString("country"));
            return firstName;
        }

        public override AuthResponse refresh(AuthToken oldToken)
        {
            string refreshToken = oldToken.refreshToken;
            if (refreshToken.IsNullOrWhiteSpace())
            {
                throw new Exception(AuthResponseStatus.REQUIRED_REFRESH_TOKEN.GetDesc());
            }
            string refreshTokenUrl = this.refreshTokenUrl(refreshToken);

            return new AuthResponse(AuthResponseStatus.SUCCESS.GetCode(), AuthResponseStatus.SUCCESS.GetDesc(), this.getToken(refreshTokenUrl));
        }

        /**
         * 获取token，适用于获取access_token和刷新token
         *
         * @param accessTokenUrl 实际请求token的地址
         * @return token对象
         */
        private AuthToken getToken(string accessTokenUrl)
        {
            var reqParams = new Dictionary<string, object>();
            reqParams.Add("Host", "www.linkedin.com");
            reqParams.Add("Content-Type", "application/x-www-form-urlencoded");

            string response = HttpUtils.RequestPost(accessTokenUrl, null, reqParams);
            string accessTokenStr = response;
            var accessTokenObject = accessTokenStr.parseObject();

            this.checkResponse(accessTokenObject);

            var authToken = new AuthToken();
            authToken.accessToken = accessTokenObject.GetParamString("access_token");
            authToken.refreshToken = accessTokenObject.GetParamString("refresh_token");
            authToken.expireIn = accessTokenObject.GetParamInt32("expire_in");

            return authToken;
        }

        /**
         * 返回带{@code state}参数的授权url，授权回调时会带上这个{@code state}
         *
         * @param state state 验证授权流程的参数，可以防止csrf
         * @return 返回授权地址
         * @since 1.9.3
         */
        public override string authorize(string state)
        {
            return UrlBuilder.fromBaseUrl(source.authorize())
                .queryParam("response_type", "code")
                .queryParam("client_id", config.clientId)
                .queryParam("redirect_uri", config.redirectUri)
                .queryParam("scope", "r_liteprofile%20r_emailaddress%20w_member_social")
                .queryParam("state", getRealState(state))
                .build();
        }

        /**
         * 返回获取userInfo的url
         *
         * @param authToken 用户授权后的token
         * @return 返回获取userInfo的url
         */
        protected override string userInfoUrl(AuthToken authToken)
        {
            return UrlBuilder.fromBaseUrl(source.userInfo())
                .queryParam("projection", "(id,firstName,lastName,profilePicture(displayImage~:playableStreams))")
                .build();
        }
        /**
       * 校验请求结果
       *
       * @param response 请求结果
       * @return 如果请求结果正常，则返回Exception
       */
        private void checkResponse(Dictionary<string, object> dic)
        {
            if (dic.ContainsKey("error"))
            {
                throw new Exception($"{dic.GetDicValue("error_description")}");
            }
        }
    }
}